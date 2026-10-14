namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lo que la puerta de los flujos (ADR 0140 F3) abre en este despliegue, para quien tiene que decidir sin
/// llamarla: el resolver de un elemento que declara su <see cref="CaminoDeCompra"/>.
/// </summary>
public interface IPuertaDeLosFlujos
{
    /// <summary>
    /// Si la puerta abre <paramref name="flujo"/>: está en <c>Synergos:Puerta:Flujos</c> y su orquestador
    /// tiene destino. Es la MISMA condición con que la puerta contesta 503 <c>puerta.flujo_no_disponible</c>.
    /// </summary>
    bool Abre(string flujo);
}
