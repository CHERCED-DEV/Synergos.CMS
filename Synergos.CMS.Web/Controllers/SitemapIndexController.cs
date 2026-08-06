using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Emite <c>/sitemap-index.xml</c> siguiendo el sitemap-index protocol
/// (<c>http://www.sitemaps.org/schemas/sitemap/0.9</c> con
/// <c>&lt;sitemapindex&gt;</c>) listando los sitemaps per-siteRoot
/// cuando un deploy hostea múltiples siteRoots bajo el mismo
/// <c>platformRoot</c>.
/// </summary>
/// <remarks>
/// Para deploys 1-host = 1-siteRoot (caso común), <c>sitemap.xml</c>
/// directo cubre la necesidad. El index existe para deploys
/// multi-siteRoot bajo el mismo hostname donde un single sitemap
/// agrega URLs de todos los sites mezcladas — el index permite a
/// Google fanout per-site.
///
/// Cada entry del index apunta a <c>/sitemap.xml?host={siteHost}</c>
/// que sigue siendo cacheable per-host (cache key existing pattern de
/// <see cref="SitemapController"/>). En la práctica, si el deploy
/// solo tiene 1 siteRoot, este endpoint emite un index con 1 sole
/// entry — válido pero redundante.
///
/// Ola 152.
/// </remarks>
[ApiController]
[Route("sitemap-index.xml")]
public sealed class SitemapIndexController : ControllerBase
{
    private const string CacheKeyPrefix = "syn:sitemap-index:";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptions<OutputCacheSettings> _cacheOptions;
    private readonly IMemoryCache _cache;

    public SitemapIndexController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptions<OutputCacheSettings> cacheOptions,
        IMemoryCache cache)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _cacheOptions = cacheOptions;
        _cache = cache;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetSitemapIndex()
    {
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

        if (bytes is null) return NotFound();
        return File(bytes, "application/xml; charset=utf-8");
    }

    private byte[]? Build(string hostBase)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return null;
        }

        var siteRoots = new List<string>();
        foreach (var root in ctx.Content.GetAtRoot())
        {
            // platformRoot es contenedor; los siteRoots son sus children directos
            // — o el root mismo si el deploy no usa platformRoot wrapper.
            if (string.Equals(root.ContentType.Alias, "platformRoot", StringComparison.Ordinal))
            {
                foreach (var child in root.Children)
                {
                    if (string.Equals(child.ContentType.Alias, "siteRoot", StringComparison.Ordinal))
                    {
                        var slug = child.UrlSegment ?? child.Name?.Replace(' ', '-').ToLowerInvariant();
                        if (!string.IsNullOrWhiteSpace(slug))
                        {
                            siteRoots.Add(slug);
                        }
                    }
                }
            }
            else if (string.Equals(root.ContentType.Alias, "siteRoot", StringComparison.Ordinal))
            {
                var slug = root.UrlSegment ?? root.Name?.Replace(' ', '-').ToLowerInvariant();
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    siteRoots.Add(slug);
                }
            }
        }

        // Si solo hay 0-1 siteRoots, igualmente emite un index válido —
        // el caller puede preferir sitemap.xml directo en ese caso.
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
            writer.WriteStartElement("sitemapindex", "http://www.sitemaps.org/schemas/sitemap/0.9");

            // Default sitemap entry — cubre el caso 1-siteRoot.
            writer.WriteStartElement("sitemap");
            writer.WriteElementString("loc", $"{hostBase}/sitemap.xml");
            writer.WriteElementString("lastmod",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK"));
            writer.WriteEndElement();

            // News sitemap entry siempre presente.
            writer.WriteStartElement("sitemap");
            writer.WriteElementString("loc", $"{hostBase}/news-sitemap.xml");
            writer.WriteElementString("lastmod",
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssK"));
            writer.WriteEndElement();

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }
}
