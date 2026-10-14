using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// Lo que la puerta abre en este despliegue (ADR 0140): un flujo que expone algún contrato incrustado,
/// abierto en <c>Synergos:Puerta:Flujos</c> y con destino para su orquestador.
/// </summary>
/// <remarks>
/// Es la misma condición con que <c>FlujosController</c> contesta 503 <c>puerta.flujo_no_disponible</c>, y lo
/// cruza <c>FlujosControllerTests</c>: quien declara el camino de compra por esto nunca manda el elemento a
/// una puerta que contestaría 503. Lee la configuración viva, como el controller.
/// </remarks>
public sealed class PuertaDeLosFlujos : IPuertaDeLosFlujos
{
    private readonly TablaDeLaPuerta _tabla;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;
    private readonly DestinosDeLaPuerta _destinos;

    public PuertaDeLosFlujos(TablaDeLaPuerta tabla, IOptionsMonitor<PuertaSettings> ajustes, DestinosDeLaPuerta destinos)
    {
        _tabla = tabla;
        _ajustes = ajustes;
        _destinos = destinos;
    }

    /// <inheritdoc />
    public bool Abre(string flujo)
    {
        var operaciones = _tabla.DelFlujo(flujo);
        return operaciones.Count > 0
            && _ajustes.CurrentValue.Flujos.ContainsKey(flujo)
            && operaciones.All(op => _destinos.Tiene(op.Orquestador));
    }
}
