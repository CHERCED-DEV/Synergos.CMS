using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTravelShell</c> → <see cref="TravelShellProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// <b>El camino de la reserva lo decide acá el CMS</b> (ADR 0140; línea 006) con la pieza de cualquier flujo del núcleo N3
/// (<see cref="ICaminoDeLosFlujos"/>): lo que declara el sitio —«directa» por defecto (P5)—; si un sitio lo deja vacío, lo
/// que declare <c>Synergos:Puerta:Flujos:viajes.reserva:Camino</c>; si no, la puerta cuando abre
/// <see cref="FlujoDeLaReserva"/>; si no, la directa. No se decide con <c>Synergos:Viajes:Mode</c>: ese interruptor dice qué
/// motor atiende la ruta directa, no si la puerta abre.
/// </remarks>
public sealed class TravelShellResolutor : IResolutorSynHost<TravelShellProps>
{
    /// <summary>
    /// El flujo de la reserva de viaje: el que coloca <c>elementSynTravelShell.cshtml</c> (y la del asistente de hotel)
    /// alrededor del elemento.
    /// </summary>
    public const string FlujoDeLaReserva = "viajes.reserva";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeTravelShell> _negocio;
    private readonly ICaminoDeLosFlujos _caminos;
    private readonly ILogger<TravelShellResolutor> _log;

    public TravelShellResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeTravelShell> negocio, ICaminoDeLosFlujos caminos,
        ILogger<TravelShellResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _caminos = caminos;
        _log = log;
    }

    public ElementoResuelto<TravelShellProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<TravelShellProps>(new TravelShellProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            BookingPath: _caminos.Decidir(FlujoDeLaReserva, negocio.BookingPath)));
    }
}
