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
/// <param name="Subtotal">Antes de impuestos: la base de una comisión de servicio.</param>
/// <param name="Total">Lo que se cobra por las líneas cotizadas.</param>
public sealed record QuoteView(Money Subtotal, Money Total);

/// <summary>Cotizar contra <c>Api.Pricing</c>.</summary>
public interface IPricingPort
{
    /// <summary>Cuánto valen esas líneas. El precio sale de la capacidad, nunca del llamador.</summary>
    Task<Result<QuoteView>> CotizarAsync(IReadOnlyList<(Ref Sujeto, int Cantidad)> lineas, CancellationToken ct);
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
