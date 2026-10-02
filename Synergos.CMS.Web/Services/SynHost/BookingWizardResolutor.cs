using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBookingWizard</c> → <see cref="BookingWizardProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class BookingWizardResolutor : IResolutorSynHost<BookingWizardProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeBookingWizard> _negocio;
    private readonly ILogger<BookingWizardResolutor> _log;

    public BookingWizardResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeBookingWizard> negocio, ILogger<BookingWizardResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<BookingWizardProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<BookingWizardProps>(new BookingWizardProps(
            DestinationLabel: editor.Texto("destinationLabel"),
            ApiBase: _negocio.Actual().ApiBase));
    }
}
