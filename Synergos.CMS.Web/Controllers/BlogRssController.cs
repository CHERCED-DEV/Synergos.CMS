using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Emite el RSS 2.0 feed del blog del sitio actual a
/// <c>/blog/rss.xml</c>. Consume <see cref="IBlogQuery"/> sin
/// duplicar lógica de query (single source of truth con
/// PostCategoryPage).
/// </summary>
/// <remarks>
/// Compatible con feed readers estándar (Feedly, Inoreader, etc.) y
/// con tags de RSS auto-discovery (link rel="alternate"
/// type="application/rss+xml") que se pueden agregar al &lt;head&gt;
/// en una micro-ola futura.
/// </remarks>
[ApiController]
[Route("blog/rss.xml")]
public sealed class BlogRssController : ControllerBase
{
    private readonly IBlogQuery _blogQuery;

    public BlogRssController(IBlogQuery blogQuery) =>
        _blogQuery = blogQuery;

    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetRss(
        [FromQuery] int maxItems = 30,
        [FromQuery(Name = "category")] string? categoryFilter = null)
    {
        var hostBase = $"{Request.Scheme}://{Request.Host}";

        var posts = _blogQuery.GetPosts(new BlogQueryRequest(
            MaxItems: Math.Clamp(maxItems, 1, 100),
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

        return File(stream.ToArray(), "application/rss+xml; charset=utf-8");
    }
}
