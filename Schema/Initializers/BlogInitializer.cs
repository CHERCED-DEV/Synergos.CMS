using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 8c — Blog Document Types.
///
/// Creates:
///   BlogHome  — blog index page (inherits PageBase compositions)
///   BlogPost  — article page (inherits PageBase compositions)
///
/// Author is created by PlatformInitializer (lives under SharedContent).
/// Category is created by PlatformInitializer but allowed as child of BlogHome.
/// BlogHome and BlogPost are children of SiteRoot (per-site blogs).
///
/// BlogHome.AllowedChildren = [Category]
/// Category.AllowedChildren = [BlogPost]
/// BlogPost has no children.
/// </summary>
internal sealed class BlogInitializer : SchemaInitializerBase
{
    private readonly IFileService _fs;

    public BlogInitializer(
        IContentTypeService cts,
        IDataTypeService dts,
        IFileService fs,
        IShortStringHelper ssh)
        : base(cts, dts, ssh)
    {
        _fs = fs;
    }

    public override void Initialize()
    {
        var blogFolderId = EnsureRootFolder("Blog");

        EnsureBlogHome(blogFolderId);
        EnsureBlogPost(blogFolderId);
        PatchBlogAllowedChildren();
    }

    // ── BlogHome ──────────────────────────────────────────────────────────────

    private void EnsureBlogHome(int folderId)
    {
        var blogHomeTemplate = EnsureTemplate("Blog Home", "BlogHome");
        var existing = Cts.Get(ContentTypeKeys.BlogHome);
        if (existing is not null)
        {
            // Old version (created by DocumentTypeInitializer) lacks the "featuredPost" property
            // that was added when Blog types were migrated exclusively here.
            var isOldVersion = !existing.PropertyTypes.Any(p => p.Alias == "featuredPost");
            if (!isOldVersion)
            {
                var dirty = false;
                if (!string.Equals(existing.Name, "Blog Home", StringComparison.Ordinal))
                { existing.Name = "Blog Home"; dirty = true; }
                dirty |= PatchTypeDescription(existing, "Listado principal del blog. Controla el contenido introductorio y la configuración general del listado.");
                dirty |= PatchIcon(existing, "icon-article");
                if (existing.ParentId != folderId)
                { existing.ParentId = folderId; dirty = true; }
                if (existing.AllowedTemplates?.Any(t => t.Id == blogHomeTemplate.Id) != true)
                { existing.AllowedTemplates = new[] { blogHomeTemplate }; existing.SetDefaultTemplate(blogHomeTemplate); dirty = true; }
                dirty |= PatchPropertyDescription(existing, "pageTitle", "Título principal del blog. Se muestra en la cabecera del listado y en el árbol de Umbraco.");
                dirty |= PatchPropertyDescription(existing, "pageSubtitle", "Texto de apoyo para presentar el enfoque o propósito del blog.");
                dirty |= PatchPropertyDescription(existing, "pageSections", "Bloques opcionales para enriquecer la parte superior del home del blog.");
                dirty |= PatchPropertyDescription(existing, "postsPerPage", "Número de entradas a mostrar por página en el listado principal.");
                dirty |= PatchPropertyDescription(existing, "featuredPost", "Entrada destacada que se mostrará con mayor protagonismo si el diseño lo contempla.");
                dirty |= PatchPropertyDescription(existing, "showCategories", "Activa la visualización de categorías en el listado del blog.");
                dirty |= PatchPropertyDescription(existing, "showTags", "Activa la visualización de etiquetas en el listado del blog.");
                dirty |= PatchPropertyDescription(existing, "listLayout", "Define la presentación visual del listado de artículos.");

                // Add CompDomLayoutProfile if missing (allows per-page layout overrides on blog pages).
                dirty |= SyncCompositions(existing,
                    ContentTypeKeys.CompCoreBase,
                    ContentTypeKeys.CompSeo,
                    ContentTypeKeys.CompDomLayout,
                    ContentTypeKeys.CompDomLayoutProfile,
                    ContentTypeKeys.CompVisibility);

                // Enable culture variation for es-ES + en-US bilingual support.
                dirty |= PatchCultureVariation(existing, "pageTitle", "pageSubtitle", "pageSections");

                // Guard: recreate pageSections if a cascading DataType delete destroyed it
                if (!existing.PropertyTypes.Any(p => p.Alias == "pageSections"))
                {
                    var contentGroup = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "content");
                    if (contentGroup is null)
                    {
                        contentGroup = Tab("Content", "content", 0);
                        existing.PropertyGroups.Add(contentGroup);
                    }
                    contentGroup.PropertyTypes!.Add(Prop("pageSections", "Page Sections", DataTypeKeys.BlockGridPageSections, 20,
                        description: "Bloques opcionales para enriquecer la parte superior del home del blog."));
                    dirty = true;
                }

                // Phase 3 — blog domain settings moved from SiteSettings to BlogHome.
                var blogConfigGroup = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "blogConfig");
                if (blogConfigGroup is null)
                {
                    blogConfigGroup = Tab("Blog Config", "blogConfig", 1);
                    existing.PropertyGroups.Add(blogConfigGroup);
                    dirty = true;
                }
                if (!existing.PropertyTypes.Any(p => p.Alias == "blogNavigation"))
                {
                    blogConfigGroup.PropertyTypes!.Add(Prop("blogNavigation", "Blog Nav", DataTypeKeys.ContentPicker, 50,
                        description: "NavigationGroup para páginas de blog (back to site + categorías)."));
                    dirty = true;
                }
                if (!existing.PropertyTypes.Any(p => p.Alias == "showAuthorBio"))
                {
                    blogConfigGroup.PropertyTypes!.Add(Prop("showAuthorBio", "Show Author Bio", DataTypeKeys.ToggleBoolean, 60,
                        description: "Muestra la bio del autor al pie de cada artículo."));
                    dirty = true;
                }
                if (!existing.PropertyTypes.Any(p => p.Alias == "showRelatedPosts"))
                {
                    blogConfigGroup.PropertyTypes!.Add(Prop("showRelatedPosts", "Show Related Posts", DataTypeKeys.ToggleBoolean, 70,
                        description: "Muestra artículos relacionados al pie de cada post."));
                    dirty = true;
                }
                if (!existing.PropertyTypes.Any(p => p.Alias == "blogCtaBlock"))
                {
                    blogConfigGroup.PropertyTypes!.Add(Prop("blogCtaBlock", "Blog CTA Block", DataTypeKeys.ContentPicker, 80,
                        description: "ReusableBlock que se muestra tras el listado de posts."));
                    dirty = true;
                }

                if (dirty) Cts.Save(existing);
                return;
            }
            Cts.Delete(existing);
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key        = ContentTypeKeys.BlogHome,
            Name       = "Blog Home",
            Description = "Listado principal del blog. Controla el contenido introductorio y la configuración general del listado.",
            Alias      = ContentTypeKeys.Aliases.BlogHome,
            Icon       = "icon-article",
            Variations = ContentVariation.Culture   // bilingual: es-ES + en-US
        };

        // Inherit same compositions as PageBase (+ LayoutProfile for per-page layout overrides)
        ct.ContentTypeComposition = new[]
        {
            ContentTypeKeys.CompCoreBase,
            ContentTypeKeys.CompSeo,
            ContentTypeKeys.CompDomLayout,
            ContentTypeKeys.CompDomLayoutProfile,
            ContentTypeKeys.CompVisibility
        }
        .Select(k => Cts.Get(k))
        .Where(c => c is not null)
        .Cast<IContentTypeComposition>()
        .ToList();

        // Tab: Content (page-level props, same as PageBase — all vary by culture)
        var tabContent = Tab("Content", "content", 0);
        var bhTitle = Prop("pageTitle",    "Page Title",    DataTypeKeys.TextTitle,             0, description: "Título principal del blog. Se muestra en la cabecera del listado y en el árbol de Umbraco.");
        bhTitle.Variations = ContentVariation.Culture;
        tabContent.PropertyTypes!.Add(bhTitle);
        var bhSubtitle = Prop("pageSubtitle", "Page Subtitle", DataTypeKeys.TextSubtitle,       10, description: "Texto de apoyo para presentar el enfoque o propósito del blog.");
        bhSubtitle.Variations = ContentVariation.Culture;
        tabContent.PropertyTypes!.Add(bhSubtitle);
        var bhSections = Prop("pageSections", "Page Sections", DataTypeKeys.BlockGridPageSections, 20,
            description: "Bloques opcionales para enriquecer la parte superior del home del blog.");
        bhSections.Variations = ContentVariation.Culture;
        tabContent.PropertyTypes!.Add(bhSections);
        ct.PropertyGroups.Add(tabContent);

        // Tab: Blog Config
        var tabBlog = Tab("Blog Config", "blogConfig", 1);
        tabBlog.PropertyTypes!.Add(Prop("postsPerPage",    "Posts Per Page",    DataTypeKeys.NumberInteger,   0,  description: "Número de entradas a mostrar por página en el listado principal."));
        tabBlog.PropertyTypes!.Add(Prop("featuredPost",    "Featured Post",     DataTypeKeys.ContentPicker,   10, description: "Entrada destacada que se mostrará con mayor protagonismo si el diseño lo contempla."));
        tabBlog.PropertyTypes!.Add(Prop("showCategories",  "Show Categories",   DataTypeKeys.ToggleBoolean,   20, description: "Activa la visualización de categorías en el listado del blog."));
        tabBlog.PropertyTypes!.Add(Prop("showTags",        "Show Tags",         DataTypeKeys.ToggleBoolean,   30, description: "Activa la visualización de etiquetas en el listado del blog."));
        tabBlog.PropertyTypes!.Add(Prop("listLayout",      "List Layout",       DataTypeKeys.SelectListLayout, 40, description: "Define la presentación visual del listado de artículos."));
        tabBlog.PropertyTypes!.Add(Prop("blogNavigation",  "Blog Nav",          DataTypeKeys.ContentPicker,   50, description: "NavigationGroup para páginas de blog (back to site + categorías)."));
        tabBlog.PropertyTypes!.Add(Prop("showAuthorBio",   "Show Author Bio",   DataTypeKeys.ToggleBoolean,   60, description: "Muestra la bio del autor al pie de cada artículo."));
        tabBlog.PropertyTypes!.Add(Prop("showRelatedPosts","Show Related Posts", DataTypeKeys.ToggleBoolean,   70, description: "Muestra artículos relacionados al pie de cada post."));
        tabBlog.PropertyTypes!.Add(Prop("blogCtaBlock",    "Blog CTA Block",    DataTypeKeys.ContentPicker,   80, description: "ReusableBlock que se muestra tras el listado de posts."));
        ct.PropertyGroups.Add(tabBlog);

        ct.AllowedTemplates = new[] { blogHomeTemplate };
        ct.SetDefaultTemplate(blogHomeTemplate);
        Cts.Save(ct);
    }

    // ── BlogPost ──────────────────────────────────────────────────────────────

    private void EnsureBlogPost(int folderId)
    {
        var blogPostTemplate = EnsureTemplate("Blog Post", "BlogPost");
        var existing = Cts.Get(ContentTypeKeys.BlogPost);
        if (existing is not null)
        {
            // Old version (created by DocumentTypeInitializer) used aliases "body" and "excerpt"
            // instead of the canonical "postBody" / "postExcerpt" expected by BlogAssembler.
            var hasOldAliases = existing.PropertyTypes.Any(p => p.Alias == "body" || p.Alias == "excerpt");
            if (!hasOldAliases)
            {
                var dirty = false;
                if (!string.Equals(existing.Name, "Blog Post", StringComparison.Ordinal))
                { existing.Name = "Blog Post"; dirty = true; }
                dirty |= PatchTypeDescription(existing, "Artículo individual del blog. Reúne contenido editorial, autoría, taxonomía y secciones complementarias.");
                dirty |= PatchIcon(existing, "icon-edit");
                if (existing.ParentId != folderId)
                { existing.ParentId = folderId; dirty = true; }
                if (existing.AllowedTemplates?.Any(t => t.Id == blogPostTemplate.Id) != true)
                { existing.AllowedTemplates = new[] { blogPostTemplate }; existing.SetDefaultTemplate(blogPostTemplate); dirty = true; }
                dirty |= SyncCompositions(existing,
                    ContentTypeKeys.CompCoreBase,
                    ContentTypeKeys.CompSeo,
                    ContentTypeKeys.CompDomLayout,
                    ContentTypeKeys.CompDomLayoutProfile,
                    ContentTypeKeys.CompVisibility,
                    ContentTypeKeys.CompContentDate,
                    ContentTypeKeys.CompContentMetadata,
                    ContentTypeKeys.CompTagging);
                dirty |= PatchPropertyDescription(existing, "postTitle", "Título principal del artículo. Se usa en la página, listados y metadatos.");
                dirty |= PatchPropertyDescription(existing, "postSubtitle", "Subtítulo o bajada breve para complementar el enfoque del artículo.");
                dirty |= PatchPropertyDescription(existing, "postBody", "Cuerpo principal del artículo en formato enriquecido.");
                dirty |= PatchPropertyDescription(existing, "postExcerpt", "Resumen corto del artículo para tarjetas, listados y vistas previas.");
                dirty |= PatchPropertyDescription(existing, "featuredImage", "Imagen principal del artículo. Se usa en cabecera, listados y compartidos.");
                dirty |= PatchPropertyDescription(existing, "postAuthor", "Autor responsable del artículo.");
                dirty |= PatchPropertyDescription(existing, "readingTime", "Tiempo estimado de lectura en minutos.");
                dirty |= PatchPropertyDescription(existing, "postType", "Clasificación editorial del artículo (article / news / tutorial / caseStudy / interview / opinion / release). Drives list filtering y variaciones de template.");
                dirty |= PatchPropertyDescription(existing, "postTags", "Etiquetas libres escritas manualmente. Úselas para filtrado flexible; para taxonomía controlada use 'Page Tags'.");
                dirty |= PatchPropertyDescription(existing, "relatedPosts", "Entradas relacionadas para recomendar lecturas adicionales al final del artículo.");
                dirty |= PatchPropertyDescription(existing, "pageSections", "Bloques opcionales para contenido adicional debajo del cuerpo principal.");

                // Non-destructive addition: patch postType on legacy blog posts that predate this property.
                if (!existing.PropertyTypes.Any(p => p.Alias == "postType"))
                {
                    var articleGroup = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "article");
                    if (articleGroup is null)
                    {
                        articleGroup = Tab("Article", "article", 0);
                        existing.PropertyGroups.Add(articleGroup);
                    }
                    articleGroup.PropertyTypes!.Add(Prop("postType", "Post Type", DataTypeKeys.SelectBlogPostType, 5,
                        description: "Clasificación editorial del artículo (article / news / tutorial / caseStudy / interview / opinion / release). Drives list filtering y variaciones de template."));
                    dirty = true;
                }

                // Enable culture variation for es-ES + en-US bilingual support.
                dirty |= PatchCultureVariation(existing,
                    "postTitle", "postSubtitle", "postBody", "postExcerpt",
                    "featuredImage", "postAuthor", "readingTime",
                    "postTags", "relatedPosts", "pageSections");

                // Guard: recreate pageSections if a cascading DataType delete destroyed it
                if (!existing.PropertyTypes.Any(p => p.Alias == "pageSections"))
                {
                    var extraGroup = existing.PropertyGroups.FirstOrDefault(g => g.Alias == "extraContent");
                    if (extraGroup is null)
                    {
                        extraGroup = Tab("Extra Content", "extraContent", 3);
                        existing.PropertyGroups.Add(extraGroup);
                    }
                    extraGroup.PropertyTypes!.Add(Prop("pageSections", "Extra Sections", DataTypeKeys.BlockGridPageSections, 0,
                        description: "Bloques opcionales para contenido adicional debajo del cuerpo principal."));
                    dirty = true;
                }

                if (dirty) Cts.Save(existing);
                return;
            }
            Cts.Delete(existing);
        }

        var ct = new ContentType(Ssh, folderId)
        {
            Key        = ContentTypeKeys.BlogPost,
            Name       = "Blog Post",
            Description = "Artículo individual del blog. Reúne contenido editorial, autoría, taxonomía y secciones complementarias.",
            Alias      = ContentTypeKeys.Aliases.BlogPost,
            Icon       = "icon-edit",
            Variations = ContentVariation.Culture   // bilingual: es-ES + en-US
        };

        // Same compositions as PageBase + ContentDate + ContentMetadata + LayoutProfile
        ct.ContentTypeComposition = new[]
        {
            ContentTypeKeys.CompCoreBase,
            ContentTypeKeys.CompSeo,
            ContentTypeKeys.CompDomLayout,
            ContentTypeKeys.CompDomLayoutProfile,
            ContentTypeKeys.CompVisibility,
            ContentTypeKeys.CompContentDate,
            ContentTypeKeys.CompContentMetadata,
            ContentTypeKeys.CompTagging
        }
        .Select(k => Cts.Get(k))
        .Where(c => c is not null)
        .Cast<IContentTypeComposition>()
        .ToList();

        // Tab: Article — all properties vary per culture
        var tabArticle = Tab("Article", "article", 0);
        void AddVariant(PropertyGroup tab, PropertyType pt) { pt.Variations = ContentVariation.Culture; tab.PropertyTypes!.Add(pt); }

        AddVariant(tabArticle, Prop("postTitle",     "Title",          DataTypeKeys.TextTitle,     0, mandatory: true, description: "Título principal del artículo. Se usa en la página, listados y metadatos."));
        AddVariant(tabArticle, Prop("postSubtitle",  "Subtitle",       DataTypeKeys.TextSubtitle,  10, description: "Subtítulo o bajada breve para complementar el enfoque del artículo."));
        // Post type is invariant — a news post is a news post in every language.
        tabArticle.PropertyTypes!.Add(Prop("postType", "Post Type", DataTypeKeys.SelectBlogPostType, 5,
            description: "Clasificación editorial del artículo (article / news / tutorial / caseStudy / interview / opinion / release). Drives list filtering y variaciones de template."));
        AddVariant(tabArticle, Prop("postBody",      "Body",           DataTypeKeys.RichTextBody,  20, description: "Cuerpo principal del artículo en formato enriquecido."));
        AddVariant(tabArticle, Prop("postExcerpt",   "Excerpt",        DataTypeKeys.TextSummary,   30, description: "Resumen corto del artículo para tarjetas, listados y vistas previas."));
        AddVariant(tabArticle, Prop("featuredImage", "Featured Image", DataTypeKeys.MediaImage,    40, description: "Imagen principal del artículo. Se usa en cabecera, listados y compartidos."));
        AddVariant(tabArticle, Prop("postAuthor",    "Author",         DataTypeKeys.ContentPicker, 50, description: "Autor responsable del artículo."));
        tabArticle.PropertyTypes!.Add(Prop("readingTime", "Reading Time", DataTypeKeys.NumberInteger, 60, description: "Tiempo estimado de lectura en minutos.")); // invariant: same value across languages
        ct.PropertyGroups.Add(tabArticle);

        // Tab: Taxonomy (category is determined by parent node in the tree)
        var tabTaxonomy = Tab("Taxonomy", "taxonomy", 1);
        AddVariant(tabTaxonomy, Prop("postTags", "Tags", DataTypeKeys.TagsContent, 0, description: "Etiquetas libres escritas manualmente. Úselas para filtrado flexible; para taxonomía controlada use 'Page Tags'."));
        ct.PropertyGroups.Add(tabTaxonomy);

        // Tab: Related
        var tabRelated = Tab("Related", "related", 2);
        AddVariant(tabRelated, Prop("relatedPosts", "Related Posts", DataTypeKeys.MultiContentPicker, 0, description: "Entradas relacionadas para recomendar lecturas adicionales al final del artículo."));
        ct.PropertyGroups.Add(tabRelated);

        // Tab: Extra Content (Block Grid for post-body blocks)
        var tabExtra = Tab("Extra Content", "extraContent", 3);
        AddVariant(tabExtra, Prop("pageSections", "Extra Sections", DataTypeKeys.BlockGridPageSections, 0,
            description: "Bloques opcionales para contenido adicional debajo del cuerpo principal."));
        ct.PropertyGroups.Add(tabExtra);

        ct.AllowedTemplates = new[] { blogPostTemplate };
        ct.SetDefaultTemplate(blogPostTemplate);
        Cts.Save(ct);
    }

    // ── Allowed children ──────────────────────────────────────────────────────

    private void PatchBlogAllowedChildren()
    {
        var siteRoot = Cts.Get(ContentTypeKeys.SiteRoot);
        var blogHome = Cts.Get(ContentTypeKeys.BlogHome);
        var blogPost = Cts.Get(ContentTypeKeys.BlogPost);
        var category = Cts.Get(ContentTypeKeys.Category);

        // SiteRoot → BlogHome  (ownership moved from DocumentTypeInitializer Phase 8)
        if (siteRoot is not null && blogHome is not null)
        {
            var siteAllowed = siteRoot.AllowedContentTypes?.ToList() ?? [];
            if (siteAllowed.All(a => a.Id.Value != blogHome.Id))
            {
                siteAllowed.Add(new ContentTypeSort(blogHome.Id, siteAllowed.Count));
                siteRoot.AllowedContentTypes = siteAllowed;
                Cts.Save(siteRoot);
            }
        }

        // BlogHome → Category only (posts live under categories)
        if (blogHome is not null && category is not null)
        {
            var homeAllowed = blogHome.AllowedContentTypes?.ToList() ?? [];

            // Remove BlogPost if previously allowed directly under BlogHome
            if (blogPost is not null)
                homeAllowed.RemoveAll(a => a.Id.Value == blogPost.Id);

            if (homeAllowed.All(a => a.Id.Value != category.Id))
                homeAllowed.Add(new ContentTypeSort(category.Id, 0));

            blogHome.AllowedContentTypes = homeAllowed;
            Cts.Save(blogHome);
        }

        // Category → BlogPost (posts are children of categories)
        if (category is not null && blogPost is not null)
        {
            var catAllowed = category.AllowedContentTypes?.ToList() ?? [];
            if (catAllowed.All(a => a.Id.Value != blogPost.Id))
            {
                catAllowed.Add(new ContentTypeSort(blogPost.Id, 0));
                category.AllowedContentTypes = catAllowed;
                Cts.Save(category);
            }
        }
    }

    // ── Unique helpers ────────────────────────────────────────────────────────

    private ITemplate EnsureTemplate(string name, string alias)
    {
        var existing = _fs.GetTemplate(alias);
        if (existing is not null) return existing;

        var template = new Template(Ssh, name, alias);
        _fs.SaveTemplate(template);
        return _fs.GetTemplate(alias)!;
    }
}
