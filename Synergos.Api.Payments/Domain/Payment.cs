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
public interface IPaymentProvider
{
    /// <summary>Cómo se llama, para dejarlo en el rastro.</summary>
    string Name { get; }

    /// <summary>Reserva el cupo. Devuelve la referencia del proveedor, o <c>null</c> si rechazó.</summary>
    string? Authorize(Money amount, Ref payer);

    /// <summary>Mueve la plata. Devuelve si pudo.</summary>
    bool Capture(string providerReference, Money amount);

    /// <summary>Devuelve plata ya capturada.</summary>
    bool Refund(string providerReference, Money amount);

    /// <summary>Libera una autorización sin cobrar.</summary>
    bool Void(string providerReference);
}
