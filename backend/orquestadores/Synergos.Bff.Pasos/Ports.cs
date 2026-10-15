using Synergos.Core;

namespace Synergos.Bff.Pasos;

// ── Los puertos: lo que un paso necesita de una capacidad, y nada más ───────
// Un paso no habla HTTP: pide por su puerto y el orquestador lo cumple con su cliente. Así el
// mismo paso de autorizar le puede servir a cualquier orquestador —hoy lo usa sólo Eventos, el
// piloto de la ADR 0140— sin conocer la URL, la llave ni el DTO de ninguno, y el orquestador sigue
// siendo el único que sabe con qué capacidad habla.
//
// Cada puerto trae SOLO lo que hoy ejecuta un flujo declarado. Lo que deshace (soltar, reponer,
// anular, devolver) sigue en el `*CompensationExecutor` de cada orquestador: un método de puerto
// sin quien lo llame es una promesa que nadie prueba.

/// <summary>Lo que vale algo según <c>Api.Pricing</c>.</summary>
/// <param name="Subtotal">Antes del descuento y de los impuestos: la base de una comisión de servicio.</param>
/// <param name="Descuento">Lo que descuenta la promoción, si se cotizó con una; si no, cero. Lo
/// calcula la capacidad sobre el subtotal, antes del impuesto.</param>
/// <param name="Impuesto">El impuesto, sobre lo ya descontado.</param>
/// <param name="Total">Lo que se cobra por las líneas cotizadas: el subtotal menos el descuento más el impuesto.</param>
public sealed record QuoteView(Money Subtotal, Money Descuento, Money Impuesto, Money Total);

/// <summary>Cotizar contra <c>Api.Pricing</c>.</summary>
public interface IPricingPort
{
    /// <summary>
    /// Cuánto valen esas líneas, con la promoción <paramref name="promocion"/> si la hay. El precio y el
    /// descuento salen de la capacidad, nunca del llamador: del llamador sólo llega el CÓDIGO.
    /// </summary>
    /// <param name="lineas">Qué y cuántas.</param>
    /// <param name="promocion">El código de la promoción, o nulo. Uno que no existe o que venció lo
    /// rechaza la capacidad con su código, antes de que nadie aparte nada.</param>
    /// <param name="ct">Cancelación.</param>
    Task<Result<QuoteView>> CotizarAsync(
        IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, string? promocion, CancellationToken ct);
}

/// <summary>Apartar y consumir existencias de un pozo contable de <c>Api.Inventory</c>.</summary>
public interface IInventoryPort
{
    /// <summary>El identificador del pozo de <paramref name="sujeto"/>. Lo genera la capacidad: no se adivina.</summary>
    Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct);

    /// <summary>Aparta unidades del pozo y devuelve el identificador del apartado.</summary>
    Task<Result<string>> ApartarAsync(
        string itemId, int cantidad, Ref paraQue, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Consume el apartado: las unidades salen del pozo de verdad.</summary>
    Task<Result<string>> ConsumirAsync(string holdId, CancellationToken ct);
}

/// <summary>Autorizar y capturar un cobro en <c>Api.Payments</c>.</summary>
public interface IPaymentsPort
{
    /// <summary>Reserva cupo en el medio de pago sin mover plata. Devuelve el identificador del cobro.</summary>
    Task<Result<string>> AutorizarAsync(
        Ref paraQue, Ref pagador, Money monto, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Mueve la plata autorizada.</summary>
    Task<Result<string>> CapturarAsync(string cobroId, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Adónde y a nombre de quién va un aviso: lo que la saga no puede llevar.</summary>
/// <param name="Correo">La dirección. Sin ella no se avisa.</param>
/// <param name="Nombre">Cómo saludar.</param>
/// <param name="Enlace">Dónde ver lo avisado.</param>
/// <param name="Sitio">Desde qué sitio se hizo.</param>
/// <remarks>
/// <b>Es efímero y no se guarda en ningún sitio</b> (ADR 0140 F3): llega en la entrada de la fase que
/// avisa, sirve para esa llamada y muere con ella. La saga no lleva datos personales a propósito, y
/// un aviso no es razón para cambiar eso.
/// </remarks>
public sealed record Contacto(string Correo, string? Nombre, string? Enlace, string? Sitio);

/// <summary>Mandar un aviso por <c>Api.Notifications</c>.</summary>
public interface INotificationsPort
{
    /// <summary>
    /// Pide el aviso con la plantilla <paramref name="plantilla"/> y esos valores. Devuelve el id de
    /// la entrega. El TEXTO no viaja: la capacidad rellena la plantilla, y la plantilla la escribe el
    /// dominio.
    /// </summary>
    Task<Result<string>> AvisarAsync(
        Ref destinatario, string direccion, string plantilla, IReadOnlyDictionary<string, string> valores,
        IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Hallar la agenda de un sujeto, apartar una ventana y confirmarla en <c>Api.Booking</c>.</summary>
public interface IBookingPort
{
    /// <summary>El identificador del recurso (la agenda) de <paramref name="sujeto"/>. Lo genera la capacidad: no se adivina.</summary>
    Task<Result<string>> HallarAsync(Ref sujeto, CancellationToken ct);

    /// <summary>Aparta la ventana en el recurso para <paramref name="paraQuien"/> y devuelve el apartado.</summary>
    Task<Result<string>> ApartarAsync(string recurso, TimeWindow ventana, Ref paraQuien, IdempotencyKey llave, CancellationToken ct);

    /// <summary>Confirma el apartado: devuelve la reserva, que es sobre lo que se cancela después.</summary>
    Task<Result<string>> ConfirmarAsync(string apartado, IdempotencyKey llave, CancellationToken ct);
}

// ── Los puertos de deshacer ─────────────────────────────────────────────────
// Separados de los de avance a propósito: los de avance los cumple también `EventosPorts`, cuyo
// oráculo los construye a mano, y su deshacer sigue yendo por `EventosCompensationExecutor`
// (supuesto P5 del núcleo). Los cumple `PuertosHttp` y los llama `DeshacerComun`: un método con
// quien lo llama, no una promesa.

/// <summary>Deshacer en <c>Api.Inventory</c>.</summary>
public interface IDeshacerExistencias
{
    /// <summary>Suelta un apartado que no se consumió.</summary>
    Task<Result<string>> SoltarAsync(string apartado, CancellationToken ct);

    /// <summary>Devuelve al pozo unidades ya consumidas: un ajuste RELATIVO con llave (#30).</summary>
    Task<Result<string>> ReponerAsync(string pozo, int cantidad, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Deshacer en <c>Api.Payments</c>.</summary>
public interface IDeshacerCobros
{
    /// <summary>Anula una autorización que no se capturó.</summary>
    Task<Result<string>> AnularAsync(string cobro, CancellationToken ct);

    /// <summary>Lo que todavía se puede devolver de un cobro (cero si ya se devolvió todo).</summary>
    Task<Result<Money>> DevolvibleAsync(string cobro, CancellationToken ct);

    /// <summary>Devuelve <paramref name="monto"/> de un cobro capturado, con llave.</summary>
    Task<Result<string>> DevolverAsync(string cobro, Money monto, string motivo, IdempotencyKey llave, CancellationToken ct);
}

/// <summary>Deshacer en <c>Api.Booking</c>.</summary>
public interface IDeshacerAgenda
{
    /// <summary>Suelta un apartado que no se confirmó.</summary>
    Task<Result<string>> LiberarAsync(string apartado, CancellationToken ct);

    /// <summary>Cancela una reserva ya confirmada.</summary>
    Task<Result<string>> CancelarAsync(string reserva, CancellationToken ct);
}
