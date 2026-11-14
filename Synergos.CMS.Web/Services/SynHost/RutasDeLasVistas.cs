using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>Dónde vive cada vista de una app en el sitio de la petición (014-despiece-tienda, I3).</summary>
public interface IRutasDeLasVistas
{
    /// <summary>
    /// Las páginas del sitio de la petición que colocan <paramref name="aliasDelBloque"/>, una por vista: la
    /// <c>vista</c> que el editor eligió en cada bloque (vacía, <paramref name="vistaPorDefecto"/>) y la URL de la
    /// página, sin las que acotan su vista. Sin petición o sin páginas, vacía.
    /// </summary>
    IReadOnlyList<RutaDeUnaVista> Del(string aliasDelBloque, string vistaPorDefecto);
}

/// <summary>
/// Las rutas de las vistas de una app, leídas de cómo está compuesto el sitio: el mismo recorrido con que el chrome
/// enlaza el carrito (<see cref="PaginaQueColoca"/>, CMS#187), desde el <c>siteRoot</c> de la página de la petición.
/// </summary>
/// <remarks>
/// <para><b>Las declara el CMS y no el código</b> (spec de 014, I3): el editor compone una página por vista y elige
/// la vista en el bloque; si mueve o renombra una página, su URL cambia aquí sola. Ningún bloque guarda la ruta de
/// otra página.</para>
///
/// <para><b>Una página por vista: la primera en el orden del árbol.</b> Dos páginas con la misma vista no son un
/// error (una puede ser un borrador del editor), pero el enlace va a una sola, siempre la misma.</para>
///
/// <para><b>Una página que acota su vista no es la página de esa vista</b> (012-blogs-social, I6): «Ofertas» es una
/// selección del catálogo y «#arquitectura» una de Explorar (<see cref="AliasDeLaSeleccion"/>). Así no se llevan el
/// enlace de todo el sitio aunque el editor las ponga primero en el árbol. Si es la única página de su vista, esa vista
/// se sigue abriendo en la página de cada uno.</para>
/// </remarks>
public sealed class RutasDeLasVistas : IRutasDeLasVistas
{
    /// <summary>El alias de la propiedad del bloque que dice qué parte de la app muestra.</summary>
    public const string AliasDeLaVista = "vista";

    /// <summary>
    /// El alias de la propiedad del bloque que acota su vista: «Qué productos muestra» en la tienda y «Qué publicaciones
    /// muestra» en Blogs.
    /// </summary>
    public const string AliasDeLaSeleccion = "seleccion";

    private const string AliasDelSitio = "siteRoot";

    private readonly IUmbracoContextAccessor _contexto;
    private readonly IPublishedUrlProvider _urls;
    private readonly IVariationContextAccessor _variacion;

    public RutasDeLasVistas(
        IUmbracoContextAccessor contexto,
        IPublishedUrlProvider urls,
        IVariationContextAccessor variacion)
    {
        _contexto = contexto;
        _urls = urls;
        _variacion = variacion;
    }

    /// <inheritdoc />
    public IReadOnlyList<RutaDeUnaVista> Del(string aliasDelBloque, string vistaPorDefecto)
    {
        if (!_contexto.TryGetUmbracoContext(out var contexto) || contexto.PublishedRequest?.PublishedContent is not { } pagina)
        {
            return [];
        }

        // El siteRoot puede colgar del platformRoot: .Root() daría la plataforma, no el sitio (_Layout.cshtml).
        var sitio = pagina.AncestorOrSelf(AliasDelSitio) ?? pagina.Root();
        return Rutas(sitio.DescendantsOrSelf<IPublishedContent>(_variacion), aliasDelBloque, vistaPorDefecto, p => _urls.GetUrl(p));
    }

    /// <summary>
    /// Las rutas de <paramref name="paginas"/>, en su orden: la primera página de cada vista. Una página sin ruta (su
    /// URL vacía, o <c>#</c>, que es lo que da Umbraco a una sin publicar) no enlaza.
    /// </summary>
    public static IReadOnlyList<RutaDeUnaVista> Rutas(
        IEnumerable<IPublishedContent> paginas,
        string aliasDelBloque,
        string vistaPorDefecto,
        Func<IPublishedContent, string?> urlDe)
    {
        ArgumentNullException.ThrowIfNull(paginas);
        ArgumentNullException.ThrowIfNull(urlDe);

        var rutas = new List<RutaDeUnaVista>();
        foreach (var pagina in paginas)
        {
            foreach (var bloque in PaginaQueColoca.Bloques(pagina, aliasDelBloque))
            {
                if (AcotaSuVista(bloque))
                {
                    continue;
                }

                var vista = VistaDe(bloque) ?? vistaPorDefecto;
                if (rutas.Exists(r => r.Vista == vista))
                {
                    continue;
                }

                var url = urlDe(pagina)?.Trim();
                if (!string.IsNullOrEmpty(url) && url != "#")
                {
                    rutas.Add(new RutaDeUnaVista(vista, url));
                }
            }
        }

        return rutas;
    }

    /// <summary>Si el bloque acota su vista: su selección tiene algo escrito o elegido.</summary>
    public static bool AcotaSuVista(IPublishedElement bloque)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        return bloque.GetProperty(AliasDeLaSeleccion)?.GetValue() switch
        {
            string una => !string.IsNullOrWhiteSpace(una),
            IEnumerable<string> varias => varias.Any(v => !string.IsNullOrWhiteSpace(v)),
            _ => false,
        };
    }

    /// <summary>
    /// La vista del bloque, recortada; <c>null</c> si el editor no eligió ninguna. Un desplegable simple llega como
    /// texto y uno múltiple como lista: de la lista vale la primera, como en <see cref="LectorDelEditor.Opciones"/>.
    /// </summary>
    public static string? VistaDe(IPublishedElement bloque)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        var vista = bloque.GetProperty(AliasDeLaVista)?.GetValue() switch
        {
            string una => una,
            IEnumerable<string> varias => varias.FirstOrDefault(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(vista) ? null : vista.Trim();
    }
}
