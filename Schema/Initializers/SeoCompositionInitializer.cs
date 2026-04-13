using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 5a — Crea la composición Seo dentro de Compositions/Seo.
///
/// Propósito:
/// Centralizar los metadatos SEO y Open Graph en una sola composición
/// reutilizable por cualquier Document Type que necesite visibilidad en
/// motores de búsqueda y redes sociales.
///
/// Principios:
///   - Separa SEO técnico (robots, canonical) de OG social (ogTitle, ogImage)
///   - No contiene lógica de renderizado — solo datos declarativos
///   - Compatible con SSR y pre-rendering
/// </summary>
internal sealed class SeoCompositionInitializer : CompositionInitializerBase
{
    public SeoCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId = EnsureRootFolder("Compositions");
        var seoFolderId  = EnsureChildFolder(rootFolderId, "Seo");

        EnsureCompSeo(seoFolderId);
    }

    // ── Comp.Seo ──────────────────────────────────────────────────────────
    // Metadatos SEO y Open Graph unificados. Permite a los editores
    // controlar visibilidad en buscadores y apariencia en redes sociales
    // sin tocar código ni configuración del servidor.
    private void EnsureCompSeo(int folderId)
    {
        if (Exists(ContentTypeKeys.CompSeo)) return;

        var ct  = CreateComposition("Comp.Seo", "compSeo", ContentTypeKeys.CompSeo, folderId, icon: "icon-search");
        var tab = Tab("Seo", "seo", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "seoTitle",
            name:        "SEO Title",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   0,
            description: "Título que aparece en el resultado de búsqueda. Si está vacío, el motor de búsqueda usará el título del nodo. Máximo 60 caracteres recomendado."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "seoDescription",
            name:        "SEO Description",
            dataTypeKey: DataTypeKeys.TextMetaDesc,
            sortOrder:   10,
            description: "Descripción del resultado en buscadores. Debe resumir el contenido de la página con claridad. Máximo 160 caracteres recomendado."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "ogTitle",
            name:        "OG Title",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   20,
            description: "Título para compartir en redes sociales (Open Graph). Si está vacío se usa el SEO Title. Máximo 70 caracteres recomendado."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "ogDescription",
            name:        "OG Description",
            dataTypeKey: DataTypeKeys.TextMetaDesc,
            sortOrder:   30,
            description: "Descripción para compartir en redes sociales. Si está vacío se usa el SEO Description."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "ogImage",
            name:        "OG Image",
            dataTypeKey: DataTypeKeys.MediaImage,
            sortOrder:   40,
            description: "Imagen usada al compartir en redes sociales. Tamaño recomendado: 1200×630px. Si está vacío el navegador elige la primera imagen disponible."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "canonical",
            name:        "Canonical URL",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   50,
            description: "URL canónica de la página. Solo definir si esta URL es duplicado de otra — le indica a Google cuál es la versión principal a indexar."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "robots",
            name:        "Robots",
            dataTypeKey: DataTypeKeys.SelectRobots,
            sortOrder:   60,
            description: "Directiva para robots de búsqueda. 'index' permite indexación normal. 'noindex' excluye la página de buscadores. 'nofollow' no sigue sus enlaces."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }
}
