using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>Si un enlace que puso el editor lleva a una página apagada (002-produccion, P11 C).</summary>
public interface IDestinosApagados
{
    /// <summary>
    /// <c>true</c> si <paramref name="url"/> lleva a una página del sitio que existe y no está publicada —ella o una
    /// de las que la contienen—: quien lo pregunta no pinta esa entrada. Lo anota en el log con el bloque y la
    /// propiedad.
    /// </summary>
    /// <param name="url">El destino tal como lo escribió el editor.</param>
    /// <param name="bloque">El bloque que lo trae, para el log.</param>
    /// <param name="propiedad">La propiedad del bloque que lo trae, para el log.</param>
    bool Apagado(string? url, IPublishedElement bloque, string propiedad);
}

/// <summary>
/// Los destinos apagados, leídos de la caché de contenido de Umbraco: la ruta no está entre lo publicado y sí entre
/// los borradores.
/// </summary>
/// <remarks>
/// <para><b>Por qué</b>: una vertical se apaga despublicando sus páginas (Salud, S-4), pero las tarjetas que la
/// anuncian viven en otras páginas, que siguen publicadas; sin esto llevarían a «página no encontrada». Encenderla es
/// volver a publicar sus páginas: las tarjetas vuelven solas.</para>
///
/// <para><b>Sólo lo que se sabe apagado</b>: una URL externa, una ruta que no es de una página (una app, la API) o
/// una página que no existe no son «apagadas», y el enlace se pinta como siempre. La URL de un enlace elegido como
/// página (con su <c>udi</c>) ya la descarta Umbraco cuando la página no está publicada.</para>
/// </remarks>
public sealed class DestinosApagados : IDestinosApagados
{
    private readonly IUmbracoContextAccessor _contexto;
    private readonly ILogger<DestinosApagados> _log;

    public DestinosApagados(IUmbracoContextAccessor contexto, ILogger<DestinosApagados> log)
    {
        _contexto = contexto;
        _log = log;
    }

    /// <inheritdoc />
    public bool Apagado(string? url, IPublishedElement bloque, string propiedad)
    {
        ArgumentNullException.ThrowIfNull(bloque);

        var ruta = RutaLocal(url);
        if (ruta is null
            || !_contexto.TryGetUmbracoContext(out var contexto)
            || contexto.Content is not { } paginas
            || !Apagada(paginas, ruta))
        {
            return false;
        }

        _log.LogInformation(
            "El bloque {Bloque} ({Tipo}) enlaza en «{Alias}» a {Url}, que no está publicada: esa entrada no se pinta.",
            bloque.Key,
            bloque.ContentType?.Alias,
            propiedad,
            url);
        return true;
    }

    /// <summary>La página de <paramref name="ruta"/> existe entre los borradores y no entre lo publicado.</summary>
    public static bool Apagada(IPublishedContentCache paginas, string ruta)
    {
        ArgumentNullException.ThrowIfNull(paginas);
        return paginas.GetByRoute(false, ruta) is null && paginas.GetByRoute(true, ruta) is not null;
    }

    /// <summary>
    /// La ruta de una página de este sitio, como la busca Umbraco: sin consulta ni ancla, en minúsculas y sin la
    /// barra final; <c>null</c> si <paramref name="url"/> no es una ruta local (vacía, externa, <c>//otro</c>,
    /// <c>mailto:</c>, un ancla o relativa).
    /// </summary>
    public static string? RutaLocal(string? url)
    {
        var texto = url?.Trim();
        if (string.IsNullOrEmpty(texto) || texto[0] != '/' || texto.StartsWith("//", StringComparison.Ordinal))
        {
            return null;
        }

        var corte = texto.IndexOfAny(['?', '#']);
        var ruta = Uri.UnescapeDataString(corte < 0 ? texto : texto[..corte]).ToLowerInvariant().TrimEnd('/');
        return ruta.Length == 0 ? "/" : ruta;
    }
}
