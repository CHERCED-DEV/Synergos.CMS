using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// Los posts editoriales que el editor autoró (<c>postPage</c>), para que el feed social sirva
/// contenido del CMS en vez de los cinco posts sembrados en C#.
/// </summary>
/// <remarks>
/// <para><b>Social tenía DOS lectores de «un post», y eso es lo que el #146 midió.</b>
/// <c>DefaultBlogQuery</c> lee <c>postPage</c> desde siempre y sirve las vistas Razor —listados,
/// RSS, sitemap, tag page—; la APP social entera (<c>BlogsController</c> → el feed, el detalle,
/// explore, guardados) lee de <see cref="IContentStream"/>, cuyo default sirve
/// <c>SocialDemoSeed.Posts</c>: cinco posts cableados en C#. O sea que la mitad editorial ya
/// salía del CMS y la mitad que el producto usa exigía un despliegue para publicar.</para>
///
/// <para><b>Esta fuente NO escribe en el feed</b>, y no es un detalle de estilo: sembrar aquí
/// sería el defecto caro del #100 —<c>GetAllAsync</c> se llama en cada lectura, así que el feed
/// crecería un item por post y por vuelta—. Esta clase lee el árbol y entrega
/// <see cref="AuthoredPost"/>; quien siembra, con huella y mapping durable, es
/// <c>CatalogContentStream</c> (Application). Hay gate (<c>SocialWiringTests</c>).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Social = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redespliegue — calco de los otros siete verticales.</para>
///
/// <para><b>Y el AUTOR viaja con el post</b>, que fue el medio hallazgo del ticket:
/// <c>StubContentStream</c> resuelve el autor de un item con <c>SocialDemoSeed.AuthorById</c>, y
/// ante un id desconocido devuelve el id como handle y como nombre. Sembrar sin traerse el
/// <c>authorPage</c> habría firmado cada tarjeta con algo que nadie escribió.</para>
/// </remarks>
public sealed class UmbracoSocialContentSource : ICatalogSource<AuthoredPost>
{
    internal const string Vertical = "Social";
    private const string PostPageAlias = "postPage";
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ILogger<UmbracoSocialContentSource> _logger;

    public UmbracoSocialContentSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ILogger<UmbracoSocialContentSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA, igual que en las otras siete fuentes: el scope de este feed no es del request
    /// sino del deploy, y vive en <c>Synergos:Catalog:Scopes:Social</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    public Task<IReadOnlyList<AuthoredPost>> GetAllAsync(
        string? scope = null,
        CancellationToken cancellationToken = default)
    {
        var nodes = ResolveNodes();
        var posts = new List<AuthoredPost>(nodes.Count);

        foreach (var node in nodes)
        {
            var proyectado = Project(node);
            if (proyectado is not null)
            {
                posts.Add(proyectado);
            }
        }

        // Los slugs repetidos sólo se ven con el listado entero en la mano.
        Report(SocialContentRules.FindSlugCollisions(posts.Select(p => p.Id)));

        var skipped = nodes.Count - posts.Count;
        if (skipped > 0)
        {
            _logger.LogWarning(
                "UmbracoSocialContentSource: se omitieron {Skipped} de {Total} postPage por datos "
                + "incompletos.",
                skipped, nodes.Count);
        }

        return Task.FromResult<IReadOnlyList<AuthoredPost>>(posts);
    }

    /// <summary>Los <c>postPage</c> publicados bajo el siteRoot configurado, o vacío.</summary>
    private IReadOnlyList<IPublishedContent> ResolveNodes()
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            _logger.LogWarning("UmbracoSocialContentSource: sin UmbracoContext; se sirve feed vacío.");
            return Array.Empty<IPublishedContent>();
        }

        var brandKey = _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // Fallar CERRADO: servir sin acotar mezclaría los posts de todos los siteRoots en un
            // mismo feed, y el de al lado puede ser otra marca.
            _logger.LogError(
                "UmbracoSocialContentSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se "
                + "siembra nada.",
                Vertical);
            return Array.Empty<IPublishedContent>();
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError(
                "UmbracoSocialContentSource: no existe un siteRoot con brandKey '{BrandKey}'.",
                brandKey);
            return Array.Empty<IPublishedContent>();
        }

        return siteRoot.DescendantsOfType(PostPageAlias).ToList();
    }

    /// <summary><c>postPage</c> → <see cref="AuthoredPost"/>, o null si no es sembrable.</summary>
    private AuthoredPost? Project(IPublishedContent node)
    {
        var slug = SocialContentRules.HandleFromSegment(node.UrlSegment ?? string.Empty);
        if (string.IsNullOrWhiteSpace(slug))
        {
            _logger.LogWarning(
                "UmbracoSocialContentSource: postPage id={Id} sin segmento de URL; se omite.",
                node.Id);
            return null;
        }

        // El resumen ES el cuerpo del item, así que sin él no hay nada honesto que poner en la
        // tarjeta: el cuerpo de un postPage son sus `sections`, y aplanarlas produciría algo que
        // parece el post y no lo es. Ver AuthoredPost.
        var excerpt = node.Value<string>("excerpt")?.Trim();
        if (string.IsNullOrWhiteSpace(excerpt))
        {
            _logger.LogWarning(
                "UmbracoSocialContentSource: postPage '{Slug}' sin excerpt; se omite. El cuerpo "
                + "del post son sus sections y no se aplanan — el feed muestra el resumen.",
                slug);
            return null;
        }

        var autorNodo = node.Value<IPublishedContent>("authorRef");
        var handle = SocialContentRules.HandleFromSegment(autorNodo?.UrlSegment ?? string.Empty);
        var nombre = autorNodo?.Value<string>("authorName")?.Trim();

        var problemasDeAutor = SocialContentRules.CheckAuthor(slug, handle, nombre);
        Report(problemasDeAutor);
        if (problemasDeAutor.Any(i => i.Level == SocialContentIssueLevel.Error))
        {
            return null;
        }

        var fecha = SocialContentRules.ParsePublishDate(slug, node.Value<string>("publishDate"));
        Report(fecha.Issues);

        var hero = node.Value<IPublishedContent>("heroImage");

        return new AuthoredPost(
            Id: slug,
            Title: node.Name ?? slug,
            Excerpt: excerpt,
            Url: node.Url(),
            HeroImageUrl: hero?.Url(),
            // El alt tal como lo escribió quien subió la imagen, o nada. NO se deriva del título:
            // lo dice el propio ContentStreamItem.MediaAlt, y la a11y del feed depende de que se
            // distinga un alt escrito de uno inventado.
            HeroImageAlt: hero?.Value<string>("altText")?.Trim() is { Length: > 0 } alt ? alt : null,
            PublishedUtc: fecha.Value,
            Kind: SocialContentRules.ArticleKind,
            Author: new AuthoredPostAuthor(
                // El actorId lleva prefijo para no chocar con los `act-*` del seed de demo: los
                // dos feeds conviven cuando alguien enciende el flag sobre un entorno que ya
                // tenía posts sembrados, y dos autores distintos con el mismo id serían uno.
                ActorId: "autor-" + handle,
                Handle: handle,
                DisplayName: nombre!,
                AvatarUrl: autorNodo?.Value<IPublishedContent>("authorAvatar")?.Url()));
    }

    private void Report(IReadOnlyList<SocialContentIssue> issues)
    {
        foreach (var issue in issues)
        {
            if (issue.Level == SocialContentIssueLevel.Error)
            {
                _logger.LogError("UmbracoSocialContentSource: {Issue}", issue.Message);
            }
            else
            {
                _logger.LogWarning("UmbracoSocialContentSource: {Issue}", issue.Message);
            }
        }
    }
}
