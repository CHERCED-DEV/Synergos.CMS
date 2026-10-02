using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTravelShell</c> → <see cref="TravelShellProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class TravelShellResolutor : IResolutorSynHost<TravelShellProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeTravelShell> _negocio;
    private readonly ILogger<TravelShellResolutor> _log;

    public TravelShellResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeTravelShell> negocio, ILogger<TravelShellResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<TravelShellProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<TravelShellProps>(new TravelShellProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: _negocio.Actual().ApiBase));
    }
}
