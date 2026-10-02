using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynAcademy</c> → <see cref="AcademyProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class AcademyResolutor : IResolutorSynHost<AcademyProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeAcademy> _negocio;
    private readonly ILogger<AcademyResolutor> _log;

    public AcademyResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeAcademy> negocio, ILogger<AcademyResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<AcademyProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<AcademyProps>(new AcademyProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: _negocio.Actual().ApiBase));
    }
}
