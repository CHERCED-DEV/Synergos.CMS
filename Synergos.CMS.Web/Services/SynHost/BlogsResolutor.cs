using Synergos.CMS.Application.Services.Impl;
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
/// <para><b>Qué publicaciones muestra</b> (012-blogs-social, I6), como «Qué productos muestra» en la tienda: la etiqueta
/// que escribe el editor es un filtro fijo de Explorar. Es texto y no una opción del DataType, así que otra etiqueta es
/// otra página, sin tocar el esquema. La página que la lleva no es la de Explorar (<see cref="RutasDeLasVistas"/>).</para>
/// </remarks>
public sealed class BlogsResolutor : IResolutorSynHost<BlogsProps>
{
    /// <summary>El bloque de la red: las páginas que lo colocan son las de sus vistas.</summary>
    public const string AliasDelBloque = "elementSynBlogs";

    /// <summary>La vista de un bloque sin elegir: el inicio, que es la red entera de antes (012, I3).</summary>
    public const string VistaPorDefecto = "inicio";

    /// <summary>«Qué publicaciones muestra» del bloque (I6): el mismo alias que la selección de la tienda.</summary>
    public const string AliasDeLaSeleccion = RutasDeLasVistas.AliasDeLaSeleccion;

    /// <summary>
    /// La etiqueta del editor como filtro fijo del catálogo de la red: sin <c>#</c>, sin espacios a los lados y en
    /// minúsculas, como salen las etiquetas del cuerpo (<see cref="PublicacionesDeLaRed.Etiquetas"/>). Vacía, ninguno.
    /// </summary>
    internal static IReadOnlyList<FiltroFijo> FiltrosDe(string? seleccion)
    {
        var etiqueta = (seleccion ?? string.Empty).Trim().TrimStart('#').Trim().ToLowerInvariant();
        return etiqueta.Length == 0 ? [] : [new FiltroFijo(CatalogoDeBlogs.CampoEtiqueta, etiqueta)];
    }

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
        var negocio = _negocio.Actual();

        return new ElementoResuelto<BlogsProps>(new BlogsProps(
            Heading: editor.Texto("heading"),
            Subheading: editor.Texto("subheading"),
            ApiBase: negocio.ApiBase,
            LecturasBase: negocio.LecturasBase,
            ModeracionBase: negocio.ModeracionBase,
            Vista: editor.Opciones(RutasDeLasVistas.AliasDeLaVista)?[0],
            Filtros: FiltrosDe(editor.Texto(AliasDeLaSeleccion)),
            Rutas: _rutas.Del(AliasDelBloque, VistaPorDefecto)));
    }
}
