using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Emite el sitemap.xml dinámico siguiendo el protocolo
/// https://www.sitemaps.org/protocol.html. Itera todo el published
/// cache, excluye DocTypes internos (mismos que
/// <see cref="SearchSettings.ExcludedDocTypeAliases"/>) y emite
/// &lt;url&gt;&lt;loc&gt;&lt;lastmod&gt;&lt;/url&gt; por cada
/// nodo público.
/// </summary>
/// <remarks>
/// Output cache via <see cref="IMemoryCache"/> con key per-host
/// (multi-brand). TTL configurable via
/// <see cref="OutputCacheSettings.SitemapMinutes"/>. Bypass total via
/// <c>Disabled=true</c>.
///
/// Para sitios con &gt; 50k URLs (límite del protocolo en un solo
/// archivo) se debería paginar a sitemap_index.xml + N children — diferido.
/// </remarks>
[ApiController]
[Route("sitemap.xml")]
public sealed class SitemapController : ControllerBase
{
    private const string CacheKeyPrefix = "syn:sitemap:";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IUmbracoContextFactory _umbracoContextFactory;
    private readonly IVariationContextAccessor _variationContextAccessor;
    private readonly ILocalizationService _localizationService;
    private readonly IOptions<SearchSettings> _searchOptions;
    private readonly IOptions<OutputCacheSettings> _cacheOptions;
    private readonly IMemoryCache _cache;

    public SitemapController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IUmbracoContextFactory umbracoContextFactory,
        IVariationContextAccessor variationContextAccessor,
        ILocalizationService localizationService,
        IOptions<SearchSettings> searchOptions,
        IOptions<OutputCacheSettings> cacheOptions,
        IMemoryCache cache)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _umbracoContextFactory = umbracoContextFactory;
        _variationContextAccessor = variationContextAccessor;
        _localizationService = localizationService;
        _searchOptions = searchOptions;
        _cacheOptions = cacheOptions;
        _cache = cache;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetSitemap()
    {
        // Umbraco no crea su contexto para una ruta con extensión (.xml): sin esto el accesor no tenía contenido y
        // la respuesta era un 404 (D-016-Q7). Si el contexto ya existe, se reutiliza.
        using var contexto = _umbracoContextFactory.EnsureUmbracoContext();
        // Tampoco hay cultura activa: sin ella, el contenido que varía por cultura no sale de GetAtRoot.
        _variationContextAccessor.VariationContext ??= new VariationContext(_localizationService.GetDefaultLanguageIsoCode());
        var hostBase = $"{Request.Scheme}://{Request.Host}";
        var cacheSettings = _cacheOptions.Value;
        var cacheKey = CacheKeyPrefix + hostBase;

        byte[]? bytes;
        if (cacheSettings.Disabled)
        {
            bytes = Build(hostBase);
        }
        else
        {
            bytes = _cache.Get<byte[]>(cacheKey);
            if (bytes is null)
            {
                bytes = Build(hostBase);
                if (bytes is not null)
                {
                    _cache.Set(cacheKey, bytes,
                        TimeSpan.FromMinutes(Math.Max(1, cacheSettings.SitemapMinutes)));
                }
            }
        }

        if (bytes is null)
        {
            return NotFound();
        }
        return File(bytes, "application/xml; charset=utf-8");
    }

    private byte[]? Build(string hostBase)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return null;
        }

        var excluded = new HashSet<string>(
            _searchOptions.Value.ExcludedDocTypeAliases,
            StringComparer.OrdinalIgnoreCase);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
        };

        using var stream = new MemoryStream();
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument();
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

            foreach (var root in ctx.Content.GetAtRoot())
            {
                EmitNode(writer, root, excluded, hostBase);
                foreach (var descendant in root.Descendants())
                {
                    EmitNode(writer, descendant, excluded, hostBase);
                }
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Sólo una página entra al sitemap: un nodo con plantilla (D-016-Q7b). Sin plantilla es configuración (alertas,
    /// notas del pie) o un dato que pinta una app (equipos, profesionales, el inventario viejo), y su URL da 404.
    /// </summary>
    public static bool EsPagina(int? templateId) => templateId is > 0;

    private static void EmitNode(
        XmlWriter writer,
        IPublishedContent node,
        HashSet<string> excluded,
        string hostBase)
    {
        if (excluded.Contains(node.ContentType.Alias) || !EsPagina(node.TemplateId))
        {
            return;
        }

        // Fuera del enrutado de Umbraco no hay cultura activa, y la URL de un nodo que varía por cultura sale «#»
        // sin ella (D-016-Q7): se le pide la de su primera cultura.
        var culture = node.ContentType.VariesByCulture() ? node.Cultures.Keys.FirstOrDefault() : null;
        var url = node.Url(culture, UrlMode.Relative);
        if (string.IsNullOrWhiteSpace(url) || url == "#")
        {
            return;
        }

        writer.WriteStartElement("url");
        writer.WriteElementString("loc", $"{hostBase}{url}");
        writer.WriteElementString(
            "lastmod",
            node.UpdateDate.ToString("yyyy-MM-ddTHH:mm:ssK"));
        writer.WriteElementString("changefreq", "weekly");
        writer.WriteEndElement();
    }
}
