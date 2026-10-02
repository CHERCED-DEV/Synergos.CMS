using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynRealty</c> → <see cref="RealtyProps"/>: lo que escribe el editor, más la
/// configuración de negocio del sitio de la petición (ADR 0137).
/// </summary>
public sealed class RealtyResolutor : IResolutorSynHost<RealtyProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeRealty> _negocio;
    private readonly ILogger<RealtyResolutor> _log;

    public RealtyResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeRealty> negocio, ILogger<RealtyResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<RealtyProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var negocio = _negocio.Actual();

        return new ElementoResuelto<RealtyProps>(new RealtyProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            DefaultRatePercent: negocio.DefaultRatePercent));
    }
}
