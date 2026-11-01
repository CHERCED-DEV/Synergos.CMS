namespace Synergos.CMS.Interfaces.Lecturas;

/// <summary>
/// Los pedidos de quien compra y el seguimiento de uno, con forma neutra de vertical (014-despiece-tienda, I4): un
/// pedido, una boleta, una reserva o una matrícula tienen referencia, número, estado, líneas y etapas.
/// </summary>
public interface ILecturaDePedidos : ILecturaDeUnVertical
{
    /// <summary>Los pedidos del miembro, en el orden del vertical. <paramref name="miembro"/> es de la sesión.</summary>
    Task<IReadOnlyList<PedidoDelMiembro>> DelMiembroAsync(Guid miembro, CancellationToken cancellationToken = default);

    /// <summary>
    /// El seguimiento de un pedido por su referencia. La referencia es la credencial de quien compró sin cuenta; si el
    /// pedido tiene dueño, otro miembro no lo lee (<see cref="ResultadoDeLaLectura.Ajena"/>), salvo el personal.
    /// </summary>
    Task<LecturaPorReferencia<SeguimientoDelPedido>> SeguimientoAsync(
        string referencia,
        IMemberAccessGate sesion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Una línea del pedido. <see cref="PuedeDevolverse"/> dice si se puede pedir su devolución y, si no,
/// <see cref="MotivoSinDevolucion"/> dice por qué (lo nombra el vertical).
/// </summary>
public sealed record LineaDelPedido(
    string Id,
    string ElementoId,
    string? OpcionId,
    string Titulo,
    int Cantidad,
    Precio Unitario,
    Precio Total,
    bool PuedeDevolverse,
    string? MotivoSinDevolucion);

/// <summary>Un pedido del miembro. <see cref="Estado"/> es el que ve quien compró, con el vocabulario del vertical.</summary>
public sealed record PedidoDelMiembro(
    string Referencia,
    string Numero,
    string Estado,
    DateTimeOffset Fecha,
    Precio Total,
    IReadOnlyList<LineaDelPedido> Lineas);

/// <summary>Los pedidos del miembro, como viajan.</summary>
public sealed record PedidosDelMiembro(IReadOnlyList<PedidoDelMiembro> Pedidos);

/// <summary>Una etapa del seguimiento. <see cref="Estado"/>: <c>hecha</c>, <c>actual</c> o <c>pendiente</c>.</summary>
public sealed record EtapaDelSeguimiento(string Clave, string Titulo, string Estado, DateTimeOffset? Fecha, string? Nota);

/// <summary>El seguimiento de un pedido: su estado y sus etapas en orden.</summary>
public sealed record SeguimientoDelPedido(
    string Referencia,
    string Numero,
    string Estado,
    string? EtapaActual,
    IReadOnlyList<EtapaDelSeguimiento> Etapas);
