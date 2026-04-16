using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Synergos.CMS.Presentation.Api;

/// <summary>
/// Emits <c>/sitemap.xml</c> by walking Umbraco's published content tree
/// and producing a sitemap.org-compliant XML document. Excludes nodes with
/// <c>umbracoNaviHide</c> or a doctype alias in the hidden list
/// (settings/navigation-only nodes). Output-cached 15 minutes.
/// </summary>
[ApiController]
public sealed class SitemapController : ControllerBase
{
    private static readonly HashSet<string> HiddenAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "siteSettings", "themeSettings", "globalSettings", "layoutProfile",
        "sharedContentFolder", "reusableBlock", "navigationGroup",
        "pageTag", "pageTagsFolder",
        "formDefinition",
        "flowSettingsRoot", "flowDefinition",
    };

    private readonly IUmbracoContextAccessor _ctx;

    public SitemapController(IUmbracoContextAccessor ctx) => _ctx = ctx;

    [HttpGet("/sitemap.xml")]
    [OutputCache(PolicyName = "SiteSettings")]
    [Produces("application/xml")]
    public IActionResult Index()
    {
        if (!_ctx.TryGetUmbracoContext(out var umbraco) || umbraco.Content is null)
            return NotFound();

        var roots = umbraco.Content.GetAtRoot();
        var buffer = new StringBuilder(8192);

        using var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Encoding = Encoding.UTF8,
            Indent   = true,
            OmitXmlDeclaration = false
        });

        writer.WriteStartDocument();
        writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

        foreach (var root in roots)
            WriteNode(writer, root);

        writer.WriteEndElement();
        writer.WriteEndDocument();
        writer.Flush();

        return Content(buffer.ToString(), "application/xml", Encoding.UTF8);
    }

    private static void WriteNode(XmlWriter writer, IPublishedContent node)
    {
        if (!node.IsVisible()) return;
        if (HiddenAliases.Contains(node.ContentType.Alias)) return;

        var url = node.Url(mode: UrlMode.Absolute);
        if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            writer.WriteStartElement("url");
            writer.WriteElementString("loc",     url);
            writer.WriteElementString("lastmod", node.UpdateDate.ToString("yyyy-MM-ddTHH:mm:ssK"));
            writer.WriteElementString("changefreq", FrequencyFor(node));
            writer.WriteElementString("priority",   PriorityFor(node).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
            writer.WriteEndElement();
        }

        foreach (var child in node.Children ?? [])
            WriteNode(writer, child);
    }

    private static string FrequencyFor(IPublishedContent node) =>
        node.ContentType.Alias switch
        {
            "blogHome"               => "daily",
            "blogPost"               => "weekly",
            "category"               => "weekly",
            "siteRoot" or "pageBase" => "weekly",
            "author"                 => "monthly",
            _                        => "monthly"
        };

    private static double PriorityFor(IPublishedContent node) =>
        node.ContentType.Alias switch
        {
            "siteRoot" => 1.0,
            "blogHome" => 0.9,
            "pageBase" => 0.8,
            "blogPost" => 0.7,
            "category" => 0.6,
            "author"   => 0.4,
            _          => 0.5
        };
}
