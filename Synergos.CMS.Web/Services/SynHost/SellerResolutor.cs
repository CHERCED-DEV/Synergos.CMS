using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynSeller</c> → <see cref="SellerProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class SellerResolutor : IResolutorSynHost<SellerProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeSeller> _negocio;
    private readonly ILogger<SellerResolutor> _log;

    public SellerResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeSeller> negocio, ILogger<SellerResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<SellerProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<SellerProps>(new SellerProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: _negocio.Actual().ApiBase));
    }
}
