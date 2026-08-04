using Synergos.Core;

namespace Synergos.Api.Payments.Domain;

/// <summary>En qué punto está un cobro.</summary>
/// <remarks>
/// <b>Autorizar y capturar están separados</b> porque son momentos distintos del negocio y
/// fallan distinto: autorizar reserva el cupo en el medio de pago —reversible y barato—, capturar
/// mueve la plata. Es el mismo razonamiento del hold de <c>Api.Booking</c>: primero el paso
/// reversible, después el que cuesta. Fundirlas obligaría a cobrar antes de saber si el resto del
/// flujo va a salir bien.
/// </remarks>
public enum PaymentStatus
{
    /// <summary>Cupo reservado en el medio de pago. Todavía no se movió plata.</summary>
    Authorized,

    /// <summary>La plata se movió.</summary>
    Captured,

    /// <summary>Autorización liberada sin cobrar. Estado final.</summary>
    Voided,

    /// <summary>El proveedor lo rechazó. Estado final.</summary>
    Failed,
}

/// <summary>Una devolución parcial o total.</summary>
public sealed record Refund(string Id, Money Amount, string? Reason, DateTimeOffset AtUtc);

/// <summary>Un cobro y su rastro.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="For">Por qué se cobra — opaco. Normalmente un pedido.</param>
/// <param name="Payer">Quién paga — opaco.</param>
/// <param name="Amount">Cuánto se autorizó.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Provider">Qué medio lo procesó.</param>
/// <param name="ProviderReference">El identificador del proveedor, para conciliar.</param>
/// <param name="Refunds">Las devoluciones aplicadas.</param>
/// <param name="AuthorizedAtUtc">Cuándo se autorizó.</param>
/// <param name="CapturedAtUtc">Cuándo se capturó.</param>
public sealed record Payment(
    string Id,
    Ref For,
    Ref Payer,
    Money Amount,
    PaymentStatus Status,
    string Provider,
    string? ProviderReference,
    IReadOnlyList<Refund> Refunds,
    DateTimeOffset AuthorizedAtUtc,
    DateTimeOffset? CapturedAtUtc = null)
{
    /// <summary>Lo ya devuelto.</summary>
    public Money Refunded => Money.Sum(Refunds.Select(r => r.Amount), Amount.Currency);

    /// <summary>Lo que todavía se puede devolver.</summary>
    public Money Refundable => Status == PaymentStatus.Captured ? Amount - Refunded : Money.Zero(Amount.Currency);
}

/// <summary>
/// El medio de pago de verdad.
/// </summary>
/// <remarks>
/// Es la costura hacia el mundo: una pasarela, un banco, un botón. El servicio no sabe cuál — y
/// por eso esta API se puede probar y desplegar sin ninguno.
/// </remarks>
/// <summary>Las cuatro formas en que termina una operación con el medio de pago.</summary>
/// <remarks>
/// <para><b>Que sean cuatro y no un booleano es el punto</b> (HU #27). Con <c>bool</c>, «el banco
/// dijo que no» y «la pasarela no contestó» son el mismo valor — y llevan a acciones opuestas: lo
/// primero no se reintenta nunca y lo segundo se reintenta siempre. Un llamador que no los puede
/// distinguir termina reintentando un rechazo (molestando al comprador con una tarjeta que ya
/// dijo no) o rindiéndose ante una caída pasajera.</para>
///
/// <para>Y <see cref="NotConfigured"/> existe para que un despliegue a medias <b>no se parezca a
/// uno que funciona</b>: sin credencial se rechaza a gritos, igual que hizo ADR 0131 con el
/// correo.</para>
/// </remarks>
public enum PaymentOutcome
{
    /// <summary>Salió.</summary>
    Ok,

    /// <summary>El proveedor dijo que no. <b>No se reintenta</b>: la respuesta no va a cambiar.</summary>
    Declined,

    /// <summary>El proveedor no contestó. <b>Sí se reintenta</b>: no se sabe qué pasó.</summary>
    Unavailable,

    /// <summary>No hay con qué cobrar. Es un defecto de despliegue, no del comprador.</summary>
    NotConfigured,
}

/// <summary>Cómo salió, y por qué.</summary>
/// <param name="Outcome">En cuál de los cuatro casos cayó.</param>
/// <param name="Reference">La referencia del proveedor, cuando la hay.</param>
/// <param name="Reason">
/// El motivo <b>del proveedor</b>, tal cual. «Fondos insuficientes» lleva a una acción y «el pago
/// falló» no lleva a ninguna: por eso viaja y no se resume.
/// </param>
public sealed record PaymentAttempt(PaymentOutcome Outcome, string? Reference, string? Reason)
{
    public bool IsOk => Outcome == PaymentOutcome.Ok;

    public static PaymentAttempt Ok(string? reference = null) => new(PaymentOutcome.Ok, reference, null);

    public static PaymentAttempt Declined(string reason) => new(PaymentOutcome.Declined, null, reason);

    public static PaymentAttempt Unavailable(string reason) => new(PaymentOutcome.Unavailable, null, reason);

    public static PaymentAttempt NotConfigured(string reason) => new(PaymentOutcome.NotConfigured, null, reason);
}

public interface IPaymentProvider
{
    /// <summary>Cómo se llama, para dejarlo en el rastro.</summary>
    string Name { get; }

    /// <summary>
    /// Si mueve plata de verdad.
    /// </summary>
    /// <remarks>
    /// Lo declara el proveedor y lo vigila un gate: un proveedor que siempre dice que sí no puede
    /// quedar seleccionado en producción. El CMS ya tuvo exactamente ese defecto —<c>Provider=Wompi</c>
    /// servía el stub en silencio— y costó una investigación.
    /// </remarks>
    bool MuevePlata { get; }

    /// <summary>Reserva el cupo.</summary>
    PaymentAttempt Authorize(Money amount, Ref payer);

    /// <summary>Mueve la plata.</summary>
    PaymentAttempt Capture(string providerReference, Money amount);

    /// <summary>Devuelve plata ya capturada.</summary>
    PaymentAttempt Refund(string providerReference, Money amount);

    /// <summary>Libera una autorización sin cobrar.</summary>
    PaymentAttempt Void(string providerReference);
}
