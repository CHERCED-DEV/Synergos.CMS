using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 2 — Creates all Synergos Content Compositions inside Compositions/Content.
///
/// Propósito del Content Kit:
/// Definir QUÉ ES el contenido, no CÓMO se ve ni CÓMO se comporta.
/// Cada composición es atómica, semántica y reutilizable en cualquier contexto
/// de renderizado: Angular, SSR, Web Components, HTML puro.
/// </summary>
internal sealed class ContentCompositionInitializer : CompositionInitializerBase
{
    public ContentCompositionInitializer(
        IContentTypeService contentTypeService,
        IDataTypeService dataTypeService,
        IShortStringHelper shortStringHelper)
        : base(contentTypeService, dataTypeService, shortStringHelper) { }

    public override void Initialize()
    {
        var rootFolderId    = EnsureRootFolder("Compositions");
        var contentFolderId = EnsureChildFolder(rootFolderId, "Content");

        EnsureContentHeading(contentFolderId);
        EnsureContentText(contentFolderId);
        EnsureContentMedia(contentFolderId);
        EnsureContentCta(contentFolderId);
        EnsureContentBadge(contentFolderId);
        EnsureContentCollection(contentFolderId);
        EnsureContentAuthor(contentFolderId);
        EnsureContentDate(contentFolderId);
        EnsureContentMetadata(contentFolderId);
        EnsureContentEmbed(contentFolderId);
    }

    // ── Comp.ContentHeading ───────────────────────────────────────────────
    // Representa la jerarquía semántica de títulos de un componente.
    // Desacoplado del estilo visual — el nivel es semántico, no decorativo.
    private void EnsureContentHeading(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentHeading)) return;

        var ct  = CreateComposition("Comp.ContentHeading", "compContentHeading", ContentTypeKeys.CompContentHeading, folderId);
        var tab = Tab("Heading", "heading", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "headingText",
            name:        "Heading Text",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   0,
            mandatory:   true,
            description: "Texto del encabezado. Define el contenido semántico del título, independiente de su presentación visual."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "headingLevel",
            name:        "Heading Level",
            dataTypeKey: DataTypeKeys.SelectHeadingLevel,
            sortOrder:   10,
            mandatory:   true,
            description: "Nivel jerárquico del encabezado (h1-h6). Determina la estructura semántica del documento, no el tamaño visual."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "headingTagOverride",
            name:        "Tag Override",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   20,
            description: "Override del tag HTML generado. Usar solo cuando el nivel semántico difiere del nivel visual requerido por el diseño."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentText ──────────────────────────────────────────────────
    // Contenido textual estructurado y jerárquico.
    // Cubre desde el título principal hasta el cuerpo enriquecido y el caption.
    private void EnsureContentText(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentText)) return;

        var ct  = CreateComposition("Comp.ContentText", "compContentText", ContentTypeKeys.CompContentText, folderId);
        var tab = Tab("Text", "text", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "title",
            name:        "Title",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   0,
            description: "Título principal del componente. Corresponde semánticamente al encabezado de mayor jerarquía del bloque."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "subtitle",
            name:        "Subtitle",
            dataTypeKey: DataTypeKeys.TextSubtitle,
            sortOrder:   10,
            description: "Subtítulo o tagline complementario al título. Aporta contexto sin reemplazar la jerarquía principal."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "body",
            name:        "Body",
            dataTypeKey: DataTypeKeys.RichTextBody,
            sortOrder:   20,
            description: "Cuerpo principal del contenido en formato enriquecido. Permite estructura editorial completa con formato básico."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "summary",
            name:        "Summary",
            dataTypeKey: DataTypeKeys.TextSummary,
            sortOrder:   30,
            description: "Resumen corto del contenido. Usado en listados, cards o previews donde el body completo no es visible."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "caption",
            name:        "Caption",
            dataTypeKey: DataTypeKeys.TextCaption,
            sortOrder:   40,
            description: "Texto de apoyo de menor jerarquía. Típicamente usado como leyenda de imagen, nota al pie o aclaración contextual."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentMedia ─────────────────────────────────────────────────
    // Contenido multimedia desacoplado del layout.
    // Future-ready: el focal point está habilitado en el DataType.
    private void EnsureContentMedia(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentMedia)) return;

        var ct  = CreateComposition("Comp.ContentMedia", "compContentMedia", ContentTypeKeys.CompContentMedia, folderId);
        var tab = Tab("Media", "media", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "media",
            name:        "Media",
            dataTypeKey: DataTypeKeys.MediaImage,
            sortOrder:   0,
            mandatory:   true,
            description: "Recurso multimedia principal del componente. Admite imagen con punto focal para recorte inteligente en diferentes viewports."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "altText",
            name:        "Alt Text",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   10,
            description: "Texto alternativo del recurso multimedia. Obligatorio para accesibilidad (WCAG 2.1). Debe describir el contenido, no el estilo."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "mediaTitle",
            name:        "Media Title",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   20,
            description: "Título asociado al recurso multimedia. Usado en tooltips, lightboxes o cuando el media se presenta de forma autónoma."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "mediaDescription",
            name:        "Media Description",
            dataTypeKey: DataTypeKeys.TextSummary,
            sortOrder:   30,
            description: "Descripción extendida del recurso multimedia. Complementa el altText con contexto editorial adicional."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentCta ───────────────────────────────────────────────────
    // Acción principal del usuario. Atómica y reutilizable en cualquier componente
    // que requiera un punto de conversión o navegación.
    private void EnsureContentCta(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentCta)) return;

        var ct  = CreateComposition("Comp.ContentCta", "compContentCta", ContentTypeKeys.CompContentCta, folderId);
        var tab = Tab("CTA", "cta", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "ctaLabel",
            name:        "Label",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   0,
            mandatory:   true,
            description: "Texto visible del CTA. Debe ser accionable y claro (ej: 'Descubrir más', 'Solicitar demo'). Evitar textos genéricos como 'Click aquí'."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "ctaLink",
            name:        "Link",
            dataTypeKey: DataTypeKeys.LinkUrl,
            sortOrder:   10,
            mandatory:   true,
            description: "URL de destino del CTA. Puede apuntar a contenido interno de Umbraco o a URLs externas."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "ctaTarget",
            name:        "Target",
            dataTypeKey: DataTypeKeys.SelectLinkTarget,
            sortOrder:   20,
            description: "Comportamiento de apertura del enlace. '_self' para navegación en la misma pestaña, '_blank' para nueva pestaña."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "ctaAriaLabel",
            name:        "Aria Label",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   30,
            description: "Descripción accesible del CTA para lectores de pantalla. Usar cuando el label visible no es suficientemente descriptivo en contexto."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentBadge ─────────────────────────────────────────────────
    // Indicador semántico compacto. Comunica estado, categoría o alerta
    // de forma visual sin comprometer la semántica del contenido principal.
    private void EnsureContentBadge(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentBadge)) return;

        var ct  = CreateComposition("Comp.ContentBadge", "compContentBadge", ContentTypeKeys.CompContentBadge, folderId);
        var tab = Tab("Badge", "badge", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "badgeText",
            name:        "Badge Text",
            dataTypeKey: DataTypeKeys.TextCaption,
            sortOrder:   0,
            mandatory:   true,
            description: "Texto del badge. Debe ser corto y preciso (ej: 'Nuevo', 'Beta', 'Destacado'). Máximo 30 caracteres recomendados."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "badgeType",
            name:        "Badge Type",
            dataTypeKey: DataTypeKeys.SelectBadgeType,
            sortOrder:   10,
            mandatory:   true,
            description: "Tipo semántico del badge: 'info' para información neutral, 'success' para confirmación o logro, 'warning' para alertas o novedades."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentCollection ────────────────────────────────────────────
    // Contenedor de elementos repetibles. El Block List se configura con
    // los Element Types permitidos una vez que estén definidos en el sistema.
    private void EnsureContentCollection(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentCollection)) return;

        var ct  = CreateComposition("Comp.ContentCollection", "compContentCollection", ContentTypeKeys.CompContentCollection, folderId);
        var tab = Tab("Collection", "collection", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "collectionTitle",
            name:        "Collection Title",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   0,
            description: "Título del grupo de elementos. Proporciona contexto editorial a la colección completa."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "collectionDescription",
            name:        "Collection Description",
            dataTypeKey: DataTypeKeys.TextSummary,
            sortOrder:   10,
            description: "Descripción o introducción de la colección. Visible opcionalmente sobre el listado de items."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "items",
            name:        "Items",
            dataTypeKey: DataTypeKeys.BlockListCollection,
            sortOrder:   20,
            description: "Elementos que componen la colección. Los tipos de bloques permitidos se configuran desde el backoffice una vez definidos los Element Types."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentAuthor ────────────────────────────────────────────────
    // Información de autoría editorial. Identifica al creador o responsable
    // del contenido con datos suficientes para presentación pública.
    private void EnsureContentAuthor(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentAuthor)) return;

        var ct  = CreateComposition("Comp.ContentAuthor", "compContentAuthor", ContentTypeKeys.CompContentAuthor, folderId);
        var tab = Tab("Author", "author", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "authorName",
            name:        "Author Name",
            dataTypeKey: DataTypeKeys.TextAuthor,
            sortOrder:   0,
            mandatory:   true,
            description: "Nombre completo del autor o contribuidor del contenido. Se muestra en bylines, tarjetas de autor y metadatos de artículo."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "authorRole",
            name:        "Author Role",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   10,
            description: "Rol o cargo del autor dentro de la organización (ej: 'Product Designer', 'Clinical Lead'). Aporta credibilidad y contexto."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "authorImage",
            name:        "Author Image",
            dataTypeKey: DataTypeKeys.MediaImage,
            sortOrder:   20,
            description: "Foto o avatar del autor. Usada en componentes de autoría, tarjetas de equipo o cabeceras de artículo."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentDate ──────────────────────────────────────────────────
    // Fechas semánticas del contenido. Separadas de Validity (que es lifecycle)
    // y de CoreAudit (que es trazabilidad técnica). Estas fechas son editoriales.
    private void EnsureContentDate(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentDate)) return;

        var ct  = CreateComposition("Comp.ContentDate", "compContentDate", ContentTypeKeys.CompContentDate, folderId);
        var tab = Tab("Dates", "dates", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "publishDate",
            name:        "Publish Date",
            dataTypeKey: DataTypeKeys.DateTimePicker,
            sortOrder:   0,
            description: "Fecha editorial de publicación del contenido. Usada en ordenamiento de listados, microdata Schema.org y metadatos Open Graph."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "updateDate",
            name:        "Last Updated",
            dataTypeKey: DataTypeKeys.DateTimePicker,
            sortOrder:   10,
            description: "Fecha de la última actualización editorial significativa. Distinta de la fecha técnica de modificación en Umbraco."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentMetadata ──────────────────────────────────────────────
    // Información auxiliar de clasificación y descubrimiento.
    // No es SEO (eso es Comp.Seo), es metadata editorial para filtrado y taxonomía.
    private void EnsureContentMetadata(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentMetadata)) return;

        var ct  = CreateComposition("Comp.ContentMetadata", "compContentMetadata", ContentTypeKeys.CompContentMetadata, folderId);
        var tab = Tab("Metadata", "metadata", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "tags",
            name:        "Tags",
            dataTypeKey: DataTypeKeys.TagsContent,
            sortOrder:   0,
            description: "Etiquetas de clasificación libre. Usadas para filtrado, búsqueda facetada y agrupación dinámica de contenido relacionado."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "category",
            name:        "Category",
            dataTypeKey: DataTypeKeys.TextIdentifier,
            sortOrder:   10,
            description: "Categoría editorial principal del contenido. Define la taxonomía primaria del elemento dentro del sistema."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "keywords",
            name:        "Keywords",
            dataTypeKey: DataTypeKeys.TextSummary,
            sortOrder:   20,
            description: "Palabras clave separadas por coma. Usadas por el sistema de búsqueda interna y como señales para personalización de contenido."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

    // ── Comp.ContentEmbed ─────────────────────────────────────────────────
    // Contenido externo embebido. Desacoplado del proveedor específico —
    // el tipo define el comportamiento de renderizado en el frontend.
    private void EnsureContentEmbed(int folderId)
    {
        if (Exists(ContentTypeKeys.CompContentEmbed)) return;

        var ct  = CreateComposition("Comp.ContentEmbed", "compContentEmbed", ContentTypeKeys.CompContentEmbed, folderId);
        var tab = Tab("Embed", "embed", 0);

        tab.PropertyTypes!.Add(Prop(
            alias:       "embedUrl",
            name:        "Embed URL",
            dataTypeKey: DataTypeKeys.TextUrl,
            sortOrder:   0,
            mandatory:   true,
            description: "URL del recurso externo a embeber. Debe ser una URL válida y accesible. El frontend valida el origen según la política de seguridad."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "embedType",
            name:        "Embed Type",
            dataTypeKey: DataTypeKeys.SelectEmbedType,
            sortOrder:   10,
            mandatory:   true,
            description: "Tipo de contenido embebido: 'video' para reproductores multimedia, 'map' para mapas interactivos, 'iframe' para contenido web genérico."));
        tab.PropertyTypes!.Add(Prop(
            alias:       "embedTitle",
            name:        "Embed Title",
            dataTypeKey: DataTypeKeys.TextTitle,
            sortOrder:   20,
            description: "Título descriptivo del contenido embebido. Requerido para accesibilidad del iframe (atributo title). Visible opcionalmente como caption."));

        ct.PropertyGroups.Add(tab);
        Cts.Save(ct);
    }

}
