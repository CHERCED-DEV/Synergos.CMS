using System.Text;
using System.Xml;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

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
/// Sin caché — el sitemap es one-shot por request, generado en memoria.
/// Para sitios con &gt; 50k URLs (límite del protocolo en un solo
/// archivo) se debería paginar a sitemap_index.xml + N children — diferido.
/// </remarks>
[ApiController]
[Route("sitemap.xml")]
public sealed class SitemapController : ControllerBase
{
    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptions<SearchSettings> _searchOptions;

    public SitemapController(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptions<SearchSettings> searchOptions)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _searchOptions = searchOptions;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult GetSitemap()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var ctx) || ctx.Content is null)
        {
            return NotFound();
        }

        var excluded = new HashSet<string>(
            _searchOptions.Value.ExcludedDocTypeAliases,
            StringComparer.OrdinalIgnoreCase);

        var hostBase = $"{Request.Scheme}://{Request.Host}";

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

        return File(stream.ToArray(), "application/xml; charset=utf-8");
    }

    private static void EmitNode(
        XmlWriter writer,
        IPublishedContent node,
        HashSet<string> excluded,
        string hostBase)
    {
        if (excluded.Contains(node.ContentType.Alias))
        {
            return;
        }

        var url = node.Url(mode: UrlMode.Relative);
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
