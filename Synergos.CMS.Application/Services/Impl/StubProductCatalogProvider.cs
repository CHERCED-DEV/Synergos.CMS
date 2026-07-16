using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IProductCatalogProvider"/> — el catálogo del marketplace. Es una
/// FACHADA: traduce la query propietaria de Tienda al motor transversal
/// (<see cref="ICatalogIndex{T}"/>) y su resultado de vuelta, y resuelve el detalle para la PDP.
/// </summary>
/// <remarks>
/// <b>Aquí ya no se busca, ni se filtra, ni se cuentan facetas, ni se ordena.</b> Todo eso
/// era código propio duplicado en los 5 verticales (regla de oro doc 25) y ahora lo hace UNA
/// impl; lo único que queda de Tienda es su <see cref="CatalogDescriptor{T}"/>, que es
/// declaración. La seam pública NO cambia de forma: <c>ProductQuery</c>/<c>ProductSearchResult</c>
/// siguen siendo los de siempre, así que ni la UI ni los tests se enteran del cambio de motor.
///
/// <para>De dónde salen los productos lo decide <see cref="ICatalogSource{T}"/>. Hoy es el
/// seed de demo; en la Ola A pasa a ser el contenido del editor cambiando UNA línea del
/// composer, sin tocar esta clase.</para>
///
/// <para>Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002).</para>
/// </remarks>
public sealed class StubProductCatalogProvider : IProductCatalogProvider
{
    private readonly ICatalogSource<CatalogProduct> _source;
    private readonly ICatalogIndex<CatalogProduct> _index;

    /// <summary>Ctor de producción: el composer inyecta la fuente y el motor.</summary>
    public StubProductCatalogProvider(ICatalogSource<CatalogProduct> source, ICatalogIndex<CatalogProduct> index)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _index = index ?? throw new ArgumentNullException(nameof(index));
    }

    /// <summary>
    /// Ctor de conveniencia: fuente de demo + motor con los defaults. Existe para que la
    /// seam se pueda construir sin cablear nada (tests y el registro por defecto).
    /// </summary>
    public StubProductCatalogProvider()
        : this(new ShopDemoCatalogSource(), new InMemoryCatalogIndex<CatalogProduct>(Descriptor, new CatalogSettings()))
    {
    }

    /// <summary>
    /// Lo que Tienda DECLARA sobre su catálogo. Cero lógica: pesos, facetas y órdenes.
    /// </summary>
    /// <remarks>
    /// Los pesos dicen que casar en el nombre importa más que en la marca, y en la marca más
    /// que en la categoría. El orden por defecto y las claves de <c>?sort=</c> son los
    /// HISTÓRICOS, para que la PLP no cambie de aspecto salvo que se escriba texto.
    /// </remarks>
    internal static CatalogDescriptor<CatalogProduct> Descriptor { get; } = new(
        idOf: p => p.Id,
        searchFields: new[]
        {
            new CatalogSearchField<CatalogProduct>(5, p => p.Name),
            new CatalogSearchField<CatalogProduct>(3, p => p.Brand),
            new CatalogSearchField<CatalogProduct>(2, p => p.Category),
        },
        // El default histórico era rating desc + nombre, bajo el alias "relevance".
        defaultOrder: products => products.OrderByDescending(p => p.Rating).ThenBy(p => p.Name, StringComparer.Ordinal),
        filters: new CatalogFilter<CatalogProduct>[]
        {
            new CatalogTermFilter<CatalogProduct>("category", "Categoría", p => new[] { p.Category }, CatalogFacetKind.SingleSelect),
            new CatalogTermFilter<CatalogProduct>("brand", "Marca", p => new[] { p.Brand }),
            new CatalogThresholdFilter<CatalogProduct>("minRating", "Calificación", p => p.Rating, new[]
            {
                // Declarados, no derivados: derivarlos daría un chip por cada rating
                // distinto (4.3, 4.7…) en vez de los tramos que el usuario espera. Y el
                // label existe porque el chip decía literalmente "4.0".
                new CatalogThresholdBucket(4.5, "4.5 estrellas o más"),
                new CatalogThresholdBucket(4.0, "4 estrellas o más"),
            }),
        },
        sorts: new Dictionary<string, Func<IEnumerable<CatalogProduct>, IOrderedEnumerable<CatalogProduct>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["price-asc"] = products => products.OrderBy(p => p.Price),
            ["price-desc"] = products => products.OrderByDescending(p => p.Price),
            ["rating"] = products => products.OrderByDescending(p => p.Rating),
            // "newest" era un Reverse() del seed. Sin fecha de alta en el modelo, lo honesto
            // es el orden inverso del catálogo, que es lo que hacía — pero ahora estable.
            ["newest"] = products => products.OrderByDescending(p => p.Id, StringComparer.Ordinal),
        });

    public async Task<ProductSearchResult> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new ProductQuery();

        var products = await _source.GetAllAsync(query.Scope, cancellationToken).ConfigureAwait(false);
        var result = _index.Search(products, ToCatalogQuery(query));

        return new ProductSearchResult(
            Products: result.Items.Select(ToSummary).ToList(),
            Facets: result.Facets.Select(ToProductFacet).ToList(),
            Total: result.Total);
    }

    /// <summary>
    /// Traduce la query de Tienda a la del motor.
    /// </summary>
    /// <remarks>
    /// <c>Category</c> es un parámetro propio de <see cref="ProductQuery"/> por historia, pero
    /// para el motor es una faceta más: se pliega dentro de <c>Filters</c>. Si el llamador
    /// manda las dos cosas (<c>?category=Hogar&amp;category=Moda</c> vía faceta), la faceta
    /// gana por ser la más específica — y no se combinan, que daría la intersección vacía.
    /// </remarks>
    private static CatalogQuery ToCatalogQuery(ProductQuery query)
    {
        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);

        if (query.Facets is { Count: > 0 })
        {
            foreach (var (field, values) in query.Facets)
            {
                if (values is { Count: > 0 })
                {
                    filters[field] = values;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Category) && !filters.ContainsKey("category"))
        {
            filters["category"] = new[] { query.Category.Trim() };
        }

        return new CatalogQuery(
            Text: query.Text,
            Filters: filters.Count > 0 ? filters : null,
            Sort: query.Sort,
            Skip: query.Skip,
            Take: query.Take);
    }

    public async Task<ProductDetail?> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        var products = await _source.GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var product = products.FirstOrDefault(p => string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            return null;
        }

        return new ProductDetail(
            Product: ToSummary(product),
            Description: product.Description,
            ImageUrls: product.Images,
            Variants: product.Variants
                .Select(v => new ProductVariant(v.VariantId, v.Name, v.Price, ShopDemoSeed.Currency, v.Stock))
                .ToList(),
            Reviews: product.Reviews
                .Select(r => new ProductReview(r.Author, r.Rating, r.Title, r.Body, r.Date))
                .ToList(),
            Questions: product.Questions
                .Select(q => new ProductQuestion(q.Asker, q.Question, q.Answer, q.Date))
                .ToList());
    }

    private static CatalogProductSummary ToSummary(CatalogProduct p) => new(
        Id: p.Id,
        Name: p.Name,
        Price: p.Price,
        Currency: ShopDemoSeed.Currency,
        Brand: p.Brand,
        Category: p.Category,
        ImageUrl: p.ImageUrl,
        Rating: p.Rating,
        ReviewCount: p.Reviews.Count,
        Stock: p.TotalStock);

    /// <summary>
    /// <see cref="CatalogFacet"/> → <see cref="ProductFacet"/>. Los records gemelos se
    /// conservan (no romper la UI ni los tests) pero ya no tienen lógica: el conteo, el
    /// drill-down y los labels los hace el motor.
    /// </summary>
    private static ProductFacet ToProductFacet(CatalogFacet f) => new(
        Field: f.Field,
        Label: f.Label,
        Values: f.Values.Select(v => new ProductFacetValue(v.Value, v.Count, v.Label)).ToList(),
        Kind: f.Kind.ToString());
}

/// <summary>
/// La fuente de demo de Tienda: sirve <see cref="ShopDemoSeed"/>. Es lo que la Ola A
/// sustituye por el contenido del editor cambiando el registro del composer.
/// </summary>
/// <remarks>
/// Ignora el <c>scope</c>: el seed es un solo catálogo, sin siteRoots. La fuente
/// Umbraco-backed SÍ tendrá que honrarlo — <c>productPage</c> lo comparten tres verticales.
/// </remarks>
public sealed class ShopDemoCatalogSource : ICatalogSource<CatalogProduct>
{
    public Task<IReadOnlyList<CatalogProduct>> GetAllAsync(string? scope = null, CancellationToken cancellationToken = default)
        => Task.FromResult(ShopDemoSeed.Products);
}
