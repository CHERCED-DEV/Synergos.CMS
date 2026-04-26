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
/// Emite el RSS 2.0 feed del blog del sitio actual a
/// <c>/blog/rss.xml</c>. Consume <see cref="IBlogQuery"/> sin
/// duplicar lógica de query (single source of truth con
/// PostCategoryPage).
/// </summary>
/// <remarks>
/// Output cache via <see cref="IMemoryCache"/> con key per-host +
/// maxItems + category. TTL configurable via
/// <see cref="OutputCacheSettings.BlogRssMinutes"/>. Bypass total
/// via <c>Disabled=true</c>.
/// </remarks>
[ApiController]
[Route("blog/rss.xml")]
public sealed class BlogRssController : ControllerBase
{
    private const string CacheKeyPrefix = "syn:blogrss:";

    private readonly IBlogQuery _blogQuery;
    private readonly IOptions<OutputCacheSettings> _cacheOptions;
    private readonly IMemoryCache _cache;

    public BlogRssController(
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
    public IActionResult GetRss(
        [FromQuery] int maxItems = 30,
        [FromQuery(Name = "category")] string? categoryFilter = null)
    {
        var hostBase = $"{Request.Scheme}://{Request.Host}";
        var cacheSettings = _cacheOptions.Value;
        var clampedMax = Math.Clamp(maxItems, 1, 100);
        var normalizedCategory = string.IsNullOrWhiteSpace(categoryFilter)
            ? string.Empty
            : categoryFilter.Trim().ToLowerInvariant();
        var cacheKey = $"{CacheKeyPrefix}{hostBase}|{clampedMax}|{normalizedCategory}";

        byte[] bytes;
        if (cacheSettings.Disabled)
        {
            bytes = Build(hostBase, clampedMax, categoryFilter);
        }
        else
        {
            bytes = _cache.Get<byte[]>(cacheKey)
                ?? CacheAndReturn(cacheKey, hostBase, clampedMax, categoryFilter, cacheSettings);
        }

        return File(bytes, "application/rss+xml; charset=utf-8");
    }

    private byte[] CacheAndReturn(
        string cacheKey,
        string hostBase,
        int maxItems,
        string? categoryFilter,
        OutputCacheSettings cacheSettings)
    {
        var bytes = Build(hostBase, maxItems, categoryFilter);
        _cache.Set(cacheKey, bytes,
            TimeSpan.FromMinutes(Math.Max(1, cacheSettings.BlogRssMinutes)));
        return bytes;
    }

    private byte[] Build(string hostBase, int maxItems, string? categoryFilter)
    {
        var posts = _blogQuery.GetPosts(new BlogQueryRequest(
            MaxItems: maxItems,
            Skip: 0,
            CategoryAliasOrName: string.IsNullOrWhiteSpace(categoryFilter)
                ? null
                : categoryFilter,
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
            writer.WriteStartElement("rss");
            writer.WriteAttributeString("version", "2.0");
            writer.WriteStartElement("channel");

            writer.WriteElementString("title",
                string.IsNullOrWhiteSpace(categoryFilter)
                    ? "Blog"
                    : $"Blog — {categoryFilter}");
            writer.WriteElementString("link", $"{hostBase}/blog/rss.xml");
            writer.WriteElementString("description",
                "Latest posts from the blog.");
            writer.WriteElementString("language", "es-CO");
            writer.WriteElementString("generator", "Synergos.CMS");
            writer.WriteElementString("lastBuildDate",
                DateTime.UtcNow.ToString("R"));

            foreach (var post in posts)
            {
                writer.WriteStartElement("item");
                writer.WriteElementString("title", post.Title);
                writer.WriteElementString("link", $"{hostBase}{post.Url}");
                writer.WriteStartElement("guid");
                writer.WriteAttributeString("isPermaLink", "true");
                writer.WriteString($"{hostBase}{post.Url}");
                writer.WriteEndElement();
                if (!string.IsNullOrWhiteSpace(post.Excerpt))
                {
                    writer.WriteElementString("description", post.Excerpt);
                }
                if (post.PublishDate.HasValue)
                {
                    writer.WriteElementString("pubDate",
                        post.PublishDate.Value.ToString("R"));
                }
                if (!string.IsNullOrWhiteSpace(post.CategoryName))
                {
                    writer.WriteElementString("category", post.CategoryName);
                }
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        return stream.ToArray();
    }
}
