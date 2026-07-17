using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// La fuente de catálogo de Tienda respaldada por el CONTENIDO del CMS: sirve los
/// <c>productPage</c> que el editor autoró, en vez del seed hardcodeado.
/// </summary>
/// <remarks>
/// <b>Es la pieza que cierra el hueco capital de T5</b> (ADR 0107): hoy se puede COMPRAR una
/// laptop que ningún editor autoró y NO se puede comprar la camiseta que sí está publicada —
/// los dos universos no comparten un solo SKU. Todo T1/T2/T3 (persistencia, ownership, pagos
/// durables, webhook) se construyó sobre mercancía que no existe como contenido.
///
/// <para>Es la ÚNICA clase de T5 que toca Umbraco. El motor y el descriptor siguen en
/// Application, sin saber que esto existe (ADR 0002).</para>
///
/// <para><b>Se activa con <c>Synergos:Catalog:Sources:Shop = cms</c></b> y el rollback es esa
/// misma línea a <c>demo</c>, sin redeploy (calco del gating de T3/ADR 0104).</para>
/// </remarks>
public sealed class UmbracoProductCatalogSource : ICatalogSource<CatalogProduct>
{
    internal const string Vertical = "Shop";
    private const string ProductPageAlias = "productPage";
    private const string ProductCategoryPageAlias = "productCategoryPage";
    private const string SiteRootAlias = "siteRoot";

    private readonly IUmbracoContextAccessor _umbracoContextAccessor;
    private readonly IOptionsMonitor<CatalogSettings> _settings;
    private readonly ILogger<UmbracoProductCatalogSource> _logger;

    public UmbracoProductCatalogSource(
        IUmbracoContextAccessor umbracoContextAccessor,
        IOptionsMonitor<CatalogSettings> settings,
        ILogger<UmbracoProductCatalogSource> logger)
    {
        _umbracoContextAccessor = umbracoContextAccessor;
        _settings = settings;
        _logger = logger;
    }

    /// <param name="scope">
    /// Se IGNORA. El scope de este catálogo no es del request: es una propiedad del deploy y
    /// vive en <c>Synergos:Catalog:Scopes:Shop</c>. Ver el remark de abajo.
    /// </param>
    /// <param name="cancellationToken">Cancelación del request en curso.</param>
    /// <remarks>
    /// <b>Por qué el scope NO viene por parámetro</b>, aunque la seam lo ofrezca: acotar aquí
    /// significa "bajo qué siteRoot vive la tienda", y eso no cambia entre requests. Tomarlo
    /// del parámetro obligaría a cada llamador a saberlo y a acertar — y el que se equivoque
    /// sirve apartamentos, en silencio. Se lee de config, que es donde vive un hecho del
    /// deploy. Si algún día hay Domains y un request pudiera decir a qué sitio pertenece,
    /// esto se complementa; hoy no los hay (verificado: el filler guarda
    /// <c>canonicalHostname</c> como contenido y nunca llama a <c>IDomainService</c>).
    /// </remarks>
    public Task<IReadOnlyList<CatalogProduct>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
    {
        if (!_umbracoContextAccessor.TryGetUmbracoContext(out var umbracoContext) || umbracoContext.Content is null)
        {
            // Fuera de un contexto de Umbraco no hay contenido que servir. Catálogo vacío y
            // no una excepción: la PLP se degrada, no revienta.
            _logger.LogWarning("UmbracoProductCatalogSource: sin UmbracoContext; se sirve catálogo vacío.");
            return Task.FromResult<IReadOnlyList<CatalogProduct>>(Array.Empty<CatalogProduct>());
        }

        var brandKey = ResolveBrandKey();
        if (string.IsNullOrWhiteSpace(brandKey))
        {
            // SIN scope NO se sirve nada. Es deliberado y es lo contrario de lo que hace el
            // código viejo (DefaultShopQuery cae a GetAtRoot() = TODOS los siteRoots): servir
            // sin acotar mezcla apartamentos y masajes entre los productos, en silencio.
            // Fallar cerrado y ruidoso es lo correcto para un error de configuración.
            _logger.LogError(
                "UmbracoProductCatalogSource: falta Synergos:Catalog:Scopes:{Vertical}. NO se sirve catálogo: " +
                "sin acotar, productPage lo comparten Tienda, Booking y Propiedades.", Vertical);
            return Task.FromResult<IReadOnlyList<CatalogProduct>>(Array.Empty<CatalogProduct>());
        }

        var siteRoot = umbracoContext.Content.GetAtRoot()
            .SelectMany(r => r.DescendantsOrSelf<IPublishedContent>())
            .FirstOrDefault(c => string.Equals(c.ContentType.Alias, SiteRootAlias, StringComparison.Ordinal)
                && string.Equals(c.Value<string>("brandKey"), brandKey, StringComparison.OrdinalIgnoreCase));

        if (siteRoot is null)
        {
            _logger.LogError("UmbracoProductCatalogSource: no existe un siteRoot con brandKey '{BrandKey}'.", brandKey);
            return Task.FromResult<IReadOnlyList<CatalogProduct>>(Array.Empty<CatalogProduct>());
        }

        var products = siteRoot
            .DescendantsOfType(ProductPageAlias)
            .Select(Project)
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        return Task.FromResult<IReadOnlyList<CatalogProduct>>(products);
    }

    private string? ResolveBrandKey()
        => _settings.CurrentValue.Scopes.TryGetValue(Vertical, out var b) && !string.IsNullOrWhiteSpace(b)
            ? b.Trim()
            : null;

    /// <summary>
    /// <c>productPage</c> → <see cref="CatalogProduct"/>. Devuelve null para lo que no es
    /// mercancía servible.
    /// </summary>
    private CatalogProduct? Project(IPublishedContent product)
    {
        var sku = product.Value<string>("productSku");
        if (string.IsNullOrWhiteSpace(sku))
        {
            // Sin SKU no hay identidad: el motor lo usaría como desempate de orden y el
            // checkout no sabría qué se compró.
            _logger.LogWarning("UmbracoProductCatalogSource: productPage id={Id} sin productSku; se omite.", product.Id);
            return null;
        }

        if (!TryParsePrice(product, sku, out var price))
        {
            return null;
        }

        var images = product.Value<IEnumerable<IPublishedContent>>("productImages")?.ToList() ?? new List<IPublishedContent>();
        var category = product.Parent;
        var categoryName = category is not null
            && string.Equals(category.ContentType.Alias, ProductCategoryPageAlias, StringComparison.Ordinal)
                ? (category.Value<string>("categoryName") ?? category.Name ?? string.Empty)
                : string.Empty;

        return new CatalogProduct(
            Id: sku,
            Name: product.Value<string>("productName") ?? product.Name ?? sku,
            Price: price,
            Brand: product.Value<string>("productBrand") ?? string.Empty,
            Category: categoryName,
            ImageUrl: images.FirstOrDefault()?.Url() ?? string.Empty,
            // Rating y reviews son UGC DERIVADO, no contenido editorial: un editor no autora
            // la review de un comprador. Salen en cero hasta que ICatalogSocialProof exista.
            Rating: 0d,
            Description: product.Value<string>("productDescriptionText") ?? string.Empty,
            Images: images.Select(i => i.Url()).ToList(),
            Variants: Array.Empty<CatalogVariant>(),
            Reviews: Array.Empty<CatalogReview>(),
            Questions: Array.Empty<CatalogQuestion>())
        {
            Stock = product.Value<int>("productStockQty"),
        };
    }

    /// <summary>
    /// El precio, o false si el texto no es INEQUÍVOCAMENTE un precio. <b>Solo dígitos.</b>
    /// </summary>
    /// <remarks>
    /// <b>Ésta es la trampa que puede costar dinero de verdad, y exige más que un TryParse.</b>
    /// <c>productPriceBase</c> es <c>Umbraco.TextBox</c> (cambiar su tipo exige Key nueva +
    /// migrar los nodos, así que no se toca), o sea que el editor teclea texto libre.
    ///
    /// <para>El código viejo hace <c>decimal.TryParse(raw, …, InvariantCulture) ? price : 0m</c>
    /// y tiene DOS agujeros, no uno:</para>
    /// <list type="number">
    ///   <item>El conocido: <c>"$89000"</c> no parsea → fallback SILENCIOSO a <c>0m</c> →
    ///   mercancía comprable a precio CERO.</item>
    ///   <item><b>El peor, y el que de verdad ocurre en Colombia:</b> <c>"49.000"</c> SÍ
    ///   parsea — en InvariantCulture el punto es separador DECIMAL, así que da <b>49</b>. El
    ///   editor escribe 49 mil pesos y la tienda lo vende por 49. No es un fallo de parseo:
    ///   es un precio plausible equivocado por 1000×, y ninguna guarda de "&gt; 0" lo ve.
    ///   Verificado en vivo: el Tote salió a 49.</item>
    /// </list>
    ///
    /// <para>Por eso no basta con parsear: el formato tiene que ser <b>inequívoco</b>. COP no
    /// usa centavos en la práctica, así que la regla es <b>solo dígitos</b> — cualquier punto,
    /// coma, símbolo o espacio se RECHAZA y se loguea. Que falte una ficha se ve al instante;
    /// un cero o un 1000× no se ven hasta que llega el extracto.</para>
    /// </remarks>
    private bool TryParsePrice(IPublishedContent product, string sku, out decimal price)
    {
        price = 0m;
        var raw = product.Value<string>("productPriceBase")?.Trim();

        // Solo dígitos: ni "49.000" (que parsearía a 49) ni "$49000" ni "49,000".
        var unambiguous = !string.IsNullOrEmpty(raw) && raw.All(char.IsAsciiDigit);

        if (unambiguous
            && decimal.TryParse(raw, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out price)
            && price > 0m)
        {
            return true;
        }

        _logger.LogError(
            "UmbracoProductCatalogSource: SKU '{Sku}' tiene productPriceBase='{Raw}', que no es un precio inequívoco. " +
            "Se OMITE del catálogo. Formato esperado: SOLO DÍGITOS, sin puntos ni símbolos (ej. 89000, no \"89.000\" — " +
            "eso se leería como 89 pesos).",
            sku, raw);
        price = 0m;
        return false;
    }
}
