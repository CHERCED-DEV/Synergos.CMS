using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// El camino de cada flujo (núcleo N3, FR-034): lo declarado por el sitio, lo declarado en
/// <c>Synergos:Puerta:Flujos:{flujo}:Camino</c>, o la regla — la puerta cuando la abre, la directa si no.
/// </summary>
/// <remarks>
/// <para><b>Lo que cada vertical cableaba a mano</b> (Eventos: su clave de negocio, su campo, su resolutor
/// con el literal del flujo) queda en un sitio: el resolver de un elemento pide el camino de su flujo y
/// nada más. Lee la configuración viva y la MISMA condición con que la puerta contesta 503
/// (<see cref="IPuertaDeLosFlujos.Abre"/>): nunca manda el elemento a una puerta cerrada.</para>
///
/// <para><b>Dos declaraciones que no coinciden</b> se dejan en el log: gana la del sitio, que es la más
/// concreta, pero alguien eligió la otra creyendo que decidía.</para>
/// </remarks>
public sealed class CaminoDeLosFlujos : ICaminoDeLosFlujos
{
    private readonly IPuertaDeLosFlujos _puerta;
    private readonly IOptionsMonitor<PuertaSettings> _ajustes;
    private readonly ILogger<CaminoDeLosFlujos> _log;

    public CaminoDeLosFlujos(IPuertaDeLosFlujos puerta, IOptionsMonitor<PuertaSettings> ajustes, ILogger<CaminoDeLosFlujos> log)
    {
        _puerta = puerta;
        _ajustes = ajustes;
        _log = log;
    }

    /// <inheritdoc />
    public string Decidir(string flujo, string? declaradoPorElSitio = null)
    {
        var delFlujo = CaminoDelFlujo.Normalizar(_ajustes.CurrentValue.Flujos.GetValueOrDefault(flujo)?.Camino);
        var delSitio = CaminoDelFlujo.Normalizar(declaradoPorElSitio);
        if (CaminoDelFlujo.SeContradicen(delSitio, delFlujo))
        {
            _log.LogWarning(
                "El camino de {Flujo} se declara dos veces y no coincide: el sitio dice {DelSitio} y Synergos:Puerta:Flujos, {DelFlujo}. Gana el del sitio.",
                flujo, delSitio, delFlujo);
        }

        return CaminoDelFlujo.Decidir(delSitio, delFlujo, _puerta.Abre(flujo));
    }
}
