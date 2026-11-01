namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// Las devoluciones de un pedido, con forma neutra de vertical (014-despiece-tienda, I4). Es la misma devolución que
/// el personal lleva a «reembolsada» por el flujo <c>*.devolucion</c> (la pieza común de NS-3, T096): la tienda, la
/// educación y los viajes la tienen con estos mismos estados.
/// </summary>
public interface ILecturaDeDevoluciones : ILecturaDeUnVertical
{
    /// <summary>
    /// Las devoluciones del pedido, por su referencia; ajeno si es de otro miembro (salvo el personal), como el
    /// seguimiento.
    /// </summary>
    Task<LecturaPorReferencia<DevolucionesDelPedido>> DelPedidoAsync(
        string referencia,
        IMemberAccessGate sesion,
        CancellationToken cancellationToken = default);
}

/// <summary>Los estados de una devolución, en el vocabulario de la lectura.</summary>
public static class EstadosDeLaDevolucion
{
    /// <summary>Pedida y sin decidir.</summary>
    public const string Solicitada = "solicitada";

    /// <summary>El personal la aceptó.</summary>
    public const string Aprobada = "aprobada";

    /// <summary>El personal la rechazó.</summary>
    public const string Rechazada = "rechazada";

    /// <summary>Lo devuelto llegó.</summary>
    public const string Recibida = "recibida";

    /// <summary>La plata volvió.</summary>
    public const string Reembolsada = "reembolsada";
}

/// <summary>Una devolución de una línea del pedido. <see cref="Estado"/> va con <see cref="EstadosDeLaDevolucion"/>.</summary>
public sealed record DevolucionDelPedido(
    string Id,
    string Linea,
    string Titulo,
    int Cantidad,
    Precio Monto,
    string Motivo,
    string Estado,
    DateTimeOffset Solicitada,
    DateTimeOffset Actualizada,
    string? Nota);

/// <summary>Las devoluciones de un pedido.</summary>
public sealed record DevolucionesDelPedido(string Referencia, IReadOnlyList<DevolucionDelPedido> Devoluciones);
