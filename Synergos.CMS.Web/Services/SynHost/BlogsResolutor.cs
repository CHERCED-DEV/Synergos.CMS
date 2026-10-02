using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBlogs</c> → <see cref="BlogsProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137).
/// </summary>
public sealed class BlogsResolutor : IResolutorSynHost<BlogsProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeBlogs> _negocio;
    private readonly ILogger<BlogsResolutor> _log;

    public BlogsResolutor(IPublishedValueFallback fallback, INegocioDelSitio<NegocioDeBlogs> negocio, ILogger<BlogsResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _log = log;
    }

    public ElementoResuelto<BlogsProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<BlogsProps>(new BlogsProps(
            Heading: editor.Texto("heading"),
            ApiBase: _negocio.Actual().ApiBase));
    }
}
