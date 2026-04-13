using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Synergos.CMS.Schema.Constants;

namespace Synergos.CMS.Schema.Initializers;

/// <summary>
/// Phase 9a (Shop) — creates the five Shop document types:
///   ShopRoot, ShopCatalogPage, ShopCategoryPage, ShopProductPage, ShopCartPage.
///
/// Extracted from DocumentTypeInitializer to keep each file under ~600 lines.
/// Called directly from DocumentTypeInitializer.Initialize() — not a separate pipeline phase.
/// </summary>
internal sealed class ShopInitializer : SchemaInitializerBase
{
    private readonly IFileService _fileService;
    private readonly string       _viewsPath;
    private readonly int          _pagesFolderId;

    public ShopInitializer(
        IContentTypeService cts,
        IDataTypeService    dts,
        IFileService        fileService,
        IShortStringHelper  ssh,
        string              viewsPath,
        int                 pagesFolderId)
        : base(cts, dts, ssh)
    {
        _fileService    = fileService;
        _viewsPath      = viewsPath;
        _pagesFolderId  = pagesFolderId;
    }

    public override void Initialize()
    {
        var shopFolderId = EnsureChildFolder(_pagesFolderId, "Shop");

        EnsureShopRoot(shopFolderId);
        EnsureShopCatalogPage(shopFolderId);
        EnsureShopCategoryPage(shopFolderId);
        EnsureShopProductPage(shopFolderId);
        EnsureShopCartPage(shopFolderId);
    }

    // ── Shop document types ───────────────────────────────────────────────────

    private void EnsureShopRoot(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ShopRoot) is not null) return;

        var template = EnsureTemplate("Shop Root", "ShopRoot");

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ShopRoot,
            Name          = "Shop Root",
            Alias         = ContentTypeKeys.Aliases.ShopRootAlias,
            Description   = "Nodo raíz de la tienda. Contiene configuración global: moneda, URL de checkout y páginas hijas del catálogo.",
            Icon          = "icon-shopping-basket",
            AllowedAsRoot = false
        };

        ct.ContentTypeComposition = new[] { ContentTypeKeys.CompCoreBase, ContentTypeKeys.CompSeo }
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        var tab = Tab("Settings", "settings", 0);
        tab.PropertyTypes!.Add(Prop("shopName",      "Shop Name",      DataTypeKeys.TextTitle,      0, mandatory: true,
            description: "Nombre de la tienda. Aparece en el título del catálogo y en los correos de confirmación de pedido."));
        tab.PropertyTypes!.Add(Prop("currency",      "Currency",       DataTypeKeys.TextIdentifier, 10,
            description: "Código ISO 4217 de la moneda (ej: COP, USD, EUR). Determina el formato de precio en todos los componentes."));
        tab.PropertyTypes!.Add(Prop("checkoutUrl",   "Checkout URL",   DataTypeKeys.LinkUrl,        20,
            description: "URL de la página de checkout. El componente cart-summary redirige aquí al hacer clic en 'Proceder al pago'."));
        tab.PropertyTypes!.Add(Prop("catalogPageId", "Catalog Page",   DataTypeKeys.ContentPicker,  30,
            description: "Referencia a la página de catálogo principal. Usada para construir links de 'volver al catálogo'."));

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
        Cts.Save(ct);
    }

    private void EnsureShopCatalogPage(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ShopCatalogPage) is not null) return;

        var template = EnsureTemplate("Shop Catalog Page", "ShopCatalogPage");

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ShopCatalogPage,
            Name          = "Shop Catalog Page",
            Alias         = ContentTypeKeys.Aliases.ShopCatalogPageAlias,
            Description   = "Página de catálogo de productos. Renderiza el componente product-grid con los filtros y categorías configurados.",
            Icon          = "icon-grid",
            AllowedAsRoot = false
        };

        ct.ContentTypeComposition = new[] { ContentTypeKeys.CompCoreBase, ContentTypeKeys.CompSeo, ContentTypeKeys.CompVisibility }
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        var tab = Tab("Content", "content", 0);
        tab.PropertyTypes!.Add(Prop("pageTitle",      "Page Title",       DataTypeKeys.TextTitle,             0, mandatory: true,
            description: "Título de la página de catálogo. Se renderiza como H1."));
        tab.PropertyTypes!.Add(Prop("pageSubtitle",   "Subtitle",         DataTypeKeys.TextSubtitle,          10,
            description: "Subtítulo o descripción corta del catálogo."));
        tab.PropertyTypes!.Add(Prop("defaultCategory","Default Category", DataTypeKeys.TextIdentifier,        20,
            description: "Alias de categoría para filtrar los productos por defecto. Dejar vacío para mostrar todos."));
        tab.PropertyTypes!.Add(Prop("columns",        "Grid Columns",     DataTypeKeys.NumberInteger,         30,
            description: "Número de columnas del grid de productos (2, 3 o 4). Por defecto: 3."));
        tab.PropertyTypes!.Add(Prop("showFilters",    "Show Filters",     DataTypeKeys.ToggleBoolean,         40,
            description: "Activa el panel de búsqueda y ordenamiento en el catálogo."));
        tab.PropertyTypes!.Add(Prop("pageSections",   "Page Sections",    DataTypeKeys.BlockGridPageSections, 50,
            description: "Bloques opcionales encima o debajo del grid de productos."));

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
        Cts.Save(ct);
    }

    private void EnsureShopCategoryPage(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ShopCategoryPage) is not null) return;

        var template = EnsureTemplate("Shop Category Page", "ShopCategoryPage");

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ShopCategoryPage,
            Name          = "Shop Category Page",
            Alias         = ContentTypeKeys.Aliases.ShopCategoryPageAlias,
            Description   = "Página de categoría de productos. Carga automáticamente los productos del alias de categoría configurado.",
            Icon          = "icon-tags",
            AllowedAsRoot = false
        };

        ct.ContentTypeComposition = new[] { ContentTypeKeys.CompCoreBase, ContentTypeKeys.CompSeo, ContentTypeKeys.CompVisibility }
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        var tab = Tab("Content", "content", 0);
        tab.PropertyTypes!.Add(Prop("pageTitle",     "Page Title",      DataTypeKeys.TextTitle,      0, mandatory: true,
            description: "Nombre de la categoría tal como aparece en el H1 y en la navegación."));
        tab.PropertyTypes!.Add(Prop("categoryAlias", "Category Alias",  DataTypeKeys.TextIdentifier, 10, mandatory: true,
            description: "Alias técnico de la categoría en /api/shop/categories. Debe coincidir exactamente con el Slug del backend."));
        tab.PropertyTypes!.Add(Prop("description",   "Description",     DataTypeKeys.TextSummary,    20,
            description: "Descripción editorial de la categoría. Aparece encima del grid de productos."));
        tab.PropertyTypes!.Add(Prop("heroImage",     "Hero Image",      DataTypeKeys.MediaImage,     30,
            description: "Imagen de cabecera de la categoría."));
        tab.PropertyTypes!.Add(Prop("columns",       "Grid Columns",    DataTypeKeys.NumberInteger,  40,
            description: "Columnas del grid (2, 3 o 4). Por defecto hereda la configuración del catálogo raíz."));

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
        Cts.Save(ct);
    }

    private void EnsureShopProductPage(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ShopProductPage) is not null) return;

        var template = EnsureTemplate("Shop Product Page", "ShopProductPage");

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ShopProductPage,
            Name          = "Shop Product Page",
            Alias         = ContentTypeKeys.Aliases.ShopProductPageAlias,
            Description   = "Página de detalle de producto (PDP). Renderiza synergos-product-detail con los datos del SKU configurado.",
            Icon          = "icon-document",
            AllowedAsRoot = false
        };

        ct.ContentTypeComposition = new[] { ContentTypeKeys.CompCoreBase, ContentTypeKeys.CompSeo, ContentTypeKeys.CompVisibility, ContentTypeKeys.CompTagging }
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        var tab = Tab("Content", "content", 0);
        tab.PropertyTypes!.Add(Prop("productSku",           "Product SKU",            DataTypeKeys.TextIdentifier,        0, mandatory: true,
            description: "SKU del producto en Synergos.API. El componente product-detail usa este valor para cargar los datos desde /api/shop/products/sku/{sku}."));
        tab.PropertyTypes!.Add(Prop("showVariantPicker",    "Show Variant Picker",    DataTypeKeys.ToggleBoolean,         10,
            description: "Muestra el selector de variantes si el producto tiene variantes (tallas, colores)."));
        tab.PropertyTypes!.Add(Prop("showQuantitySelector", "Show Quantity Selector", DataTypeKeys.ToggleBoolean,         20,
            description: "Muestra el selector de cantidad en la PDP."));
        tab.PropertyTypes!.Add(Prop("showRating",           "Show Rating",            DataTypeKeys.ToggleBoolean,         30,
            description: "Muestra las estrellas de valoración si el producto tiene rating."));
        tab.PropertyTypes!.Add(Prop("layout",               "Layout",                 DataTypeKeys.SelectPageLayout,      40,
            description: "Disposición de la PDP: imageLeft (por defecto), imageRight o imageTop."));
        tab.PropertyTypes!.Add(Prop("relatedSections",      "Related Content",        DataTypeKeys.BlockGridPageSections, 50,
            description: "Bloques de contenido complementario debajo de la ficha de producto (descripciones ampliadas, specs, reviews)."));

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
        Cts.Save(ct);
    }

    private void EnsureShopCartPage(int folderId)
    {
        if (Cts.Get(ContentTypeKeys.ShopCartPage) is not null) return;

        var template = EnsureTemplate("Shop Cart Page", "ShopCartPage");

        var ct = new ContentType(Ssh, folderId)
        {
            Key           = ContentTypeKeys.ShopCartPage,
            Name          = "Shop Cart Page",
            Alias         = ContentTypeKeys.Aliases.ShopCartPageAlias,
            Description   = "Página de carrito y checkout. Renderiza synergos-cart-summary en modo página completa.",
            Icon          = "icon-shopping-basket-remove",
            AllowedAsRoot = false
        };

        ct.ContentTypeComposition = new[] { ContentTypeKeys.CompCoreBase, ContentTypeKeys.CompSeo }
            .Select(k => Cts.Get(k))
            .Where(c => c is not null)
            .Cast<IContentTypeComposition>()
            .ToList();

        var tab = Tab("Settings", "settings", 0);
        tab.PropertyTypes!.Add(Prop("showCoupon",          "Show Coupon Field",     DataTypeKeys.ToggleBoolean, 0,
            description: "Activa el campo de código de descuento en el carrito."));
        tab.PropertyTypes!.Add(Prop("continueShoppingUrl", "Continue Shopping URL", DataTypeKeys.LinkUrl,       10,
            description: "URL a la que se redirige al hacer clic en 'Continuar comprando'. Por defecto: /tienda."));
        tab.PropertyTypes!.Add(Prop("emptyCartMessage",    "Empty Cart Message",    DataTypeKeys.TextSubtitle,  20,
            description: "Mensaje editorial cuando el carrito está vacío. Si está en blanco se usa la traducción del Diccionario."));

        ct.PropertyGroups.Add(tab);
        ct.AllowedTemplates = new[] { template };
        ct.SetDefaultTemplate(template);
        Cts.Save(ct);
    }

    // ── Template helper ───────────────────────────────────────────────────────

    /// <summary>
    /// Creates or retrieves an Umbraco template, always syncing DB content with the
    /// physical .cshtml source file to prevent Umbraco from overwriting custom views.
    /// </summary>
    private ITemplate EnsureTemplate(string name, string alias)
    {
        var physicalPath  = Path.Combine(_viewsPath, $"{alias}.cshtml");
        var sourceContent = System.IO.File.Exists(physicalPath)
            ? System.IO.File.ReadAllText(physicalPath)
            : null;

        var existing = _fileService.GetTemplate(alias);
        if (existing is not null)
        {
            if (sourceContent is not null && existing.Content != sourceContent)
            {
                existing.Content = sourceContent;
                _fileService.SaveTemplate(existing);
                System.IO.File.WriteAllText(physicalPath, sourceContent);
            }
            return existing;
        }

        var template = new Template(Ssh, name, alias);
        if (sourceContent is not null)
            template.Content = sourceContent;

        _fileService.SaveTemplate(template);

        if (sourceContent is not null)
            System.IO.File.WriteAllText(physicalPath, sourceContent);

        return _fileService.GetTemplate(alias)!;
    }
}
