using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Emite el news-sitemap.xml siguiendo el Google News Sitemap Protocol
/// (<c>http://www.google.com/schemas/sitemap-news/0.9</c>) — solo
/// listings de posts editoriales (postPage) publicados dentro de la
/// ventana configurable (default 48h).
/// </summary>
/// <remarks>
/// Diferenciado del sitemap.xml general: Google News indexa más
/// agresivo este endpoint y solo quiere ver entries con
/// <c>publication_date</c> reciente. Posts más viejos que la ventana
/// no se listan (Google los descarta del News index igualmente).
///
/// Output cache via <see cref="IMemoryCache"/> con key per-host. TTL
/// configurable via <see cref="OutputCacheSettings.NewsSitemapMinutes"/>.
/// Bypass total via <c>Disabled=true</c>.
///
/// Ola 138.
/// </remarks>
[ApiController]
[Route("news-sitemap.xml")]
public sealed class NewsSitemapController : ControllerBase
{
    private const string CacheKeyPrefix = "syn:news-sitemap:";
    private const string NewsNs = "http://www.google.com/schemas/sitemap-news/0.9";
    private const string SitemapNs = "http://www.sitemaps.org/schemas/sitemap/0.9";

    private readonly IBlogQuery _blogQuery;
    private readonly IOptions<OutputCacheSettings> _cacheOptions;
    private readonly IMemoryCache _cache;

    public NewsSitemapController(
        IBlogQuery blogQuery,
        IOptions<OutputCacheSettings> cacheOptions,
        IMemoryCache cache)
    {
        _blogQuery = blogQuery;
        _cacheOptions = cacheOptions;
        _cache = cache;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetNewsSitemap()
    {
        var hostBase = $"{Request.Scheme}://{Request.Host}";
        var cacheSettings = _cacheOptions.Value;
        var cacheKey = CacheKeyPrefix + hostBase;

        byte[] bytes;
        if (cacheSettings.Disabled)
        {
            bytes = Build(hostBase, cacheSettings.NewsSitemapWindowHours);
        }
        else
        {
            bytes = _cache.Get<byte[]>(cacheKey)
                ?? CacheAndReturn(cacheKey, hostBase, cacheSettings);
        }

        return File(bytes, "application/xml; charset=utf-8");
    }

    private byte[] CacheAndReturn(
        string cacheKey,
        string hostBase,
        OutputCacheSettings cacheSettings)
    {
        var bytes = Build(hostBase, cacheSettings.NewsSitemapWindowHours);
        _cache.Set(cacheKey, bytes,
            TimeSpan.FromMinutes(Math.Max(1, cacheSettings.NewsSitemapMinutes)));
        return bytes;
    }

    private byte[] Build(string hostBase, int windowHours)
    {
        var window = TimeSpan.FromHours(Math.Max(1, windowHours));
        var cutoff = DateTime.UtcNow - window;
        var posts = _blogQuery.GetPosts(new BlogQueryRequest(
            MaxItems: 1000,
            Skip: 0,
            CategoryAliasOrName: null,
            TagsCsv: null));

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
            writer.WriteStartElement("urlset", SitemapNs);
            writer.WriteAttributeString("xmlns", "news", null, NewsNs);

            var publicationName = string.IsNullOrWhiteSpace(Request.Host.Host)
                ? "Synergos"
                : Request.Host.Host;

            foreach (var post in posts)
            {
                if (!post.PublishDate.HasValue) continue;
                if (post.PublishDate.Value < cutoff) continue;

                writer.WriteStartElement("url");
                writer.WriteElementString("loc", $"{hostBase}{post.Url}");

                writer.WriteStartElement("news", "news", NewsNs);

                writer.WriteStartElement("news", "publication", NewsNs);
                writer.WriteElementString("news", "name", NewsNs, publicationName);
                writer.WriteElementString("news", "language", NewsNs, "es");
                writer.WriteEndElement();

                writer.WriteElementString(
                    "news", "publication_date", NewsNs,
                    post.PublishDate.Value.ToString("yyyy-MM-ddTHH:mm:ssK"));
                writer.WriteElementString("news", "title", NewsNs, post.Title);

                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }
}
