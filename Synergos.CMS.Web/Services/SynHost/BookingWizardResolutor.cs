using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBookingWizard</c> → <see cref="BookingWizardProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
/// <remarks>
/// El camino lo decide la misma regla que el portal de viajes, sobre el MISMO flujo
/// (<see cref="TravelShellResolutor.FlujoDeLaReserva"/>): una estadía es un viaje de un ítem (006-P4).
/// </remarks>
public sealed class BookingWizardResolutor : IResolutorSynHost<BookingWizardProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeBookingWizard> _negocio;
    private readonly ICaminoDeLosFlujos _caminos;
    private readonly ILogger<BookingWizardResolutor> _log;

    public BookingWizardResolutor(
        IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeBookingWizard> negocio, ICaminoDeLosFlujos caminos,
        ILogger<BookingWizardResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _caminos = caminos;
        _log = log;
    }

    public ElementoResuelto<BookingWizardProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<BookingWizardProps>(new BookingWizardProps(
            DestinationLabel: editor.Texto("destinationLabel"),
            ApiBase: negocio.ApiBase,
            BookingPath: _caminos.Decidir(TravelShellResolutor.FlujoDeLaReserva, negocio.BookingPath)));
    }
}
