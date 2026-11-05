using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBlogs</c> → <see cref="BlogsProps"/>: lo que escribe el editor, más dónde vive
/// la API para el sitio de la petición (ADR 0137), qué parte de la red es la página y dónde viven las demás.
/// </summary>
/// <remarks>
/// <b>La vista y las rutas</b> (012-blogs-social, I3), como en la tienda: la vista la elige el editor en el bloque, y
/// las rutas salen de las páginas del sitio que colocan este bloque (<see cref="IRutasDeLasVistas"/>), sin código
/// nuevo ahí.
/// </remarks>
public sealed class BlogsResolutor : IResolutorSynHost<BlogsProps>
{
    /// <summary>El bloque de la red: las páginas que lo colocan son las de sus vistas.</summary>
    public const string AliasDelBloque = "elementSynBlogs";

    /// <summary>La vista de un bloque sin elegir: el inicio, que es la red entera de antes (012, I3).</summary>
    public const string VistaPorDefecto = "inicio";

    private readonly IPublishedValueFallback _fallback;
    private readonly INegocioDelSitio<NegocioDeBlogs> _negocio;
    private readonly IRutasDeLasVistas _rutas;
    private readonly ILogger<BlogsResolutor> _log;

    public BlogsResolutor(
        IPublishedValueFallback fallback,
        INegocioDelSitio<NegocioDeBlogs> negocio,
        IRutasDeLasVistas rutas,
        ILogger<BlogsResolutor> log)
    {
        _fallback = fallback;
        _negocio = negocio;
        _rutas = rutas;
        _log = log;
    }

    public ElementoResuelto<BlogsProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<BlogsProps>(new BlogsProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: _negocio.Actual().ApiBase,
            Vista: editor.Opciones(RutasDeLasVistas.AliasDeLaVista)?[0],
            Rutas: _rutas.Del(AliasDelBloque, VistaPorDefecto)));
    }
}
