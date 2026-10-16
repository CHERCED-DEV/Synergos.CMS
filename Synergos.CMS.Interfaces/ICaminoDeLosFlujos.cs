namespace Synergos.CMS.Interfaces;

/// <summary>
/// El camino de un flujo en este despliegue (núcleo N3, FR-034): para el resolver de un elemento que lo
/// declara a su participante, sin cablear la regla ni su configuración en cada vertical.
/// </summary>
public interface ICaminoDeLosFlujos
{
    /// <summary>
    /// El camino de <paramref name="flujo"/> por la regla de <see cref="CaminoDelFlujo"/>.
    /// </summary>
    /// <param name="flujo">La clave del flujo (<c>salud.cita</c>).</param>
    /// <param name="declaradoPorElSitio">Lo que declara la configuración de negocio del sitio, si la
    /// funcionalidad la tiene (es la de antes de N3: <c>PurchasePath</c> de Eventos). Gana sobre la del flujo.</param>
    string Decidir(string flujo, string? declaradoPorElSitio = null);
}
