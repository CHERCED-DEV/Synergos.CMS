using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IProductCatalogProvider"/> — catálogo del marketplace STUB
/// para que el dominio Tienda corra end-to-end en demo sin un índice de búsqueda
/// real (mismo patrón stub-first que <c>StubRoomAvailabilityProvider</c> /
/// <c>StubFlightAvailabilityProvider</c>). Sirve un catálogo sembrado en memoria
/// (varias categorías × productos × variantes × reviews × preguntas), aplica el
/// filtro de texto/categoría/facetas, deriva las facetas con conteos, y resuelve
/// el detalle de un producto para la PDP.
/// </summary>
/// <remarks>
/// Lógica pura y determinista en <c>Synergos.CMS.Application</c> — cero
/// dependencia de Umbraco/AspNetCore (ADR 0002). El adapter real (Examine/Lucene
/// sobre <c>productPage</c>, o Algolia/Elastic) implementa la misma seam y se
/// registra en su lugar vía el composer sin tocar el motor ni el módulo Angular.
/// </remarks>
public sealed class StubProductCatalogProvider : IProductCatalogProvider
{
    private const string Currency = "COP";

    // Catálogo sembrado: 6 productos en 3 categorías (Tecnología / Hogar /
    // Deportes), con marcas, rating, stock, variantes, reviews y Q&A.
    private static readonly IReadOnlyList<CatalogProduct> Catalog = new[]
    {
        new CatalogProduct(
            Id: "tec-laptop-pro-14",
            Name: "Laptop Pro 14\" M-series",
            Price: 5_200_000m,
            Brand: "Aurora",
            Category: "Tecnología",
            ImageUrl: "/media/shop/laptop-pro-14.jpg",
            Rating: 4.7,
            Description: "Ultrabook de 14 pulgadas con pantalla de alta resolución, 18 horas de batería y chip de bajo consumo. Ideal para trabajo y creación.",
            Images: new[] { "/media/shop/laptop-pro-14.jpg", "/media/shop/laptop-pro-14-2.jpg", "/media/shop/laptop-pro-14-3.jpg" },
            Variants: new[]
            {
                new CatalogVariant("tec-laptop-pro-14-16-512", "16 GB RAM · 512 GB SSD", 5_200_000m, 12),
                new CatalogVariant("tec-laptop-pro-14-32-1tb", "32 GB RAM · 1 TB SSD", 6_400_000m, 5),
            },
            Reviews: new[]
            {
                new CatalogReview("Camila R.", 5, "Excelente rendimiento", "La batería dura todo el día y es muy liviana.", new DateOnly(2026, 5, 12)),
                new CatalogReview("Andrés M.", 4, "Muy buena, pero cara", "Cumple de sobra; el precio es alto pero vale.", new DateOnly(2026, 4, 28)),
            },
            Questions: new[]
            {
                new CatalogQuestion("Laura P.", "¿Trae cargador USB-C en la caja?", "Sí, incluye cargador de 67W USB-C.", new DateOnly(2026, 5, 1)),
                new CatalogQuestion("Diego S.", "¿La RAM es ampliable?", null, new DateOnly(2026, 5, 20)),
            }),

        new CatalogProduct(
            Id: "tec-audifonos-anc",
            Name: "Audífonos Inalámbricos ANC",
            Price: 680_000m,
            Brand: "Sonido",
            Category: "Tecnología",
            ImageUrl: "/media/shop/audifonos-anc.jpg",
            Rating: 4.5,
            Description: "Audífonos over-ear con cancelación activa de ruido, 30 horas de autonomía y carga rápida.",
            Images: new[] { "/media/shop/audifonos-anc.jpg", "/media/shop/audifonos-anc-2.jpg" },
            Variants: new[]
            {
                new CatalogVariant("tec-audifonos-anc-negro", "Negro medianoche", 680_000m, 40),
                new CatalogVariant("tec-audifonos-anc-blanco", "Blanco perla", 680_000m, 18),
            },
            Reviews: new[]
            {
                new CatalogReview("Valentina G.", 5, "La cancelación de ruido es real", "Perfectos para viajar en bus o avión.", new DateOnly(2026, 6, 2)),
            },
            Questions: Array.Empty<CatalogQuestion>()),

        new CatalogProduct(
            Id: "hog-cafetera-espresso",
            Name: "Cafetera Espresso Automática",
            Price: 1_350_000m,
            Brand: "Barista",
            Category: "Hogar",
            ImageUrl: "/media/shop/cafetera-espresso.jpg",
            Rating: 4.3,
            Description: "Cafetera espresso con molino integrado, espumador de leche y panel táctil. 15 bares de presión.",
            Images: new[] { "/media/shop/cafetera-espresso.jpg", "/media/shop/cafetera-espresso-2.jpg" },
            Variants: new[]
            {
                new CatalogVariant("hog-cafetera-espresso-inox", "Acero inoxidable", 1_350_000m, 9),
            },
            Reviews: new[]
            {
                new CatalogReview("Felipe O.", 4, "Buen café en casa", "El molino integrado se agradece. Limpieza un poco tediosa.", new DateOnly(2026, 5, 8)),
                new CatalogReview("Marcela T.", 5, "Como de cafetería", "El espumador de leche es excelente.", new DateOnly(2026, 4, 15)),
            },
            Questions: new[]
            {
                new CatalogQuestion("Juan C.", "¿Sirve para café molido o solo en grano?", "Acepta ambos: grano (molino integrado) y molido.", new DateOnly(2026, 4, 20)),
            }),

        new CatalogProduct(
            Id: "hog-aspiradora-robot",
            Name: "Aspiradora Robot con Mapeo",
            Price: 1_790_000m,
            Brand: "CleanBot",
            Category: "Hogar",
            ImageUrl: "/media/shop/aspiradora-robot.jpg",
            Rating: 4.1,
            Description: "Robot aspirador y trapeador con mapeo láser LiDAR, app móvil y vaciado automático.",
            Images: new[] { "/media/shop/aspiradora-robot.jpg" },
            Variants: Array.Empty<CatalogVariant>(),
            Reviews: new[]
            {
                new CatalogReview("Sandra L.", 4, "Mapea bien la casa", "Evita escaleras y muebles. La app es intuitiva.", new DateOnly(2026, 6, 10)),
            },
            Questions: Array.Empty<CatalogQuestion>())
        { Stock = 22 },

        new CatalogProduct(
            Id: "dep-bici-mtb-29",
            Name: "Bicicleta MTB Rin 29",
            Price: 2_450_000m,
            Brand: "Trail",
            Category: "Deportes",
            ImageUrl: "/media/shop/bici-mtb-29.jpg",
            Rating: 4.6,
            Description: "Mountain bike de aluminio rin 29, 21 velocidades, frenos de disco hidráulicos y suspensión delantera.",
            Images: new[] { "/media/shop/bici-mtb-29.jpg", "/media/shop/bici-mtb-29-2.jpg" },
            Variants: new[]
            {
                new CatalogVariant("dep-bici-mtb-29-m", "Talla M (1.65-1.75 m)", 2_450_000m, 7),
                new CatalogVariant("dep-bici-mtb-29-l", "Talla L (1.75-1.85 m)", 2_450_000m, 4),
            },
            Reviews: new[]
            {
                new CatalogReview("Carlos V.", 5, "Excelente relación precio-calidad", "Los frenos hidráulicos responden muy bien.", new DateOnly(2026, 5, 30)),
            },
            Questions: new[]
            {
                new CatalogQuestion("Mónica A.", "¿Viene armada?", "Llega 90% armada; solo manubrio y pedales.", new DateOnly(2026, 5, 25)),
            }),

        new CatalogProduct(
            Id: "dep-tenis-running",
            Name: "Tenis de Running Ultraligeros",
            Price: 420_000m,
            Brand: "Veloz",
            Category: "Deportes",
            ImageUrl: "/media/shop/tenis-running.jpg",
            Rating: 4.4,
            Description: "Zapatillas de running con espuma de retorno de energía, malla transpirable y suela de alta tracción.",
            Images: new[] { "/media/shop/tenis-running.jpg", "/media/shop/tenis-running-2.jpg" },
            Variants: new[]
            {
                new CatalogVariant("dep-tenis-running-39", "Talla 39", 420_000m, 15),
                new CatalogVariant("dep-tenis-running-41", "Talla 41", 420_000m, 20),
                new CatalogVariant("dep-tenis-running-43", "Talla 43", 420_000m, 11),
            },
            Reviews: new[]
            {
                new CatalogReview("Paula H.", 5, "Muy cómodos", "Los uso para entrenar 5K, excelente amortiguación.", new DateOnly(2026, 6, 5)),
                new CatalogReview("Esteban R.", 4, "Buenos pero tallan pequeño", "Pedir media talla más.", new DateOnly(2026, 5, 18)),
            },
            Questions: Array.Empty<CatalogQuestion>()),
    };

    public Task<ProductSearchResult> SearchAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        query ??= new ProductQuery();

        // 1) Filtro de texto (nombre/marca/categoría) — case-insensitive.
        IEnumerable<CatalogProduct> filtered = Catalog;
        if (!string.IsNullOrWhiteSpace(query.Text))
        {
            var text = query.Text.Trim();
            filtered = filtered.Where(p =>
                p.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.Brand.Contains(text, StringComparison.OrdinalIgnoreCase)
                || p.Category.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        // 2) Filtro de categoría exacta (case-insensitive).
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var category = query.Category.Trim();
            filtered = filtered.Where(p => string.Equals(p.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        // 3) Filtro por facetas seleccionadas (brand / minRating). Las claves
        //    canónicas las refleja la misma seam en GetFacets — la UI manda de
        //    vuelta lo que recibió. Claves desconocidas se ignoran (forward-compat).
        if (query.Facets is { Count: > 0 })
        {
            foreach (var (field, value) in query.Facets)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }
                filtered = field.ToLowerInvariant() switch
                {
                    "brand" => filtered.Where(p => string.Equals(p.Brand, value, StringComparison.OrdinalIgnoreCase)),
                    "category" => filtered.Where(p => string.Equals(p.Category, value, StringComparison.OrdinalIgnoreCase)),
                    "minrating" when double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var min)
                        => filtered.Where(p => p.Rating >= min),
                    _ => filtered,
                };
            }
        }

        var matched = filtered.ToList();

        // 4) Orden.
        matched = (query.Sort?.ToLowerInvariant()) switch
        {
            "price-asc" => matched.OrderBy(p => p.Price).ToList(),
            "price-desc" => matched.OrderByDescending(p => p.Price).ToList(),
            "rating" => matched.OrderByDescending(p => p.Rating).ToList(),
            "newest" => matched.AsEnumerable().Reverse().ToList(),
            _ => matched.OrderByDescending(p => p.Rating).ThenBy(p => p.Name, StringComparer.Ordinal).ToList(), // relevance
        };

        var products = matched.Select(ToSummary).ToList();
        var facets = BuildFacets(matched);

        return Task.FromResult(new ProductSearchResult(products, facets, products.Count));
    }

    public Task<ProductDetail?> GetProductAsync(string productId, CancellationToken cancellationToken = default)
    {
        var product = Catalog.FirstOrDefault(p =>
            string.Equals(p.Id, productId, StringComparison.OrdinalIgnoreCase));
        if (product is null)
        {
            return Task.FromResult<ProductDetail?>(null);
        }

        var detail = new ProductDetail(
            Product: ToSummary(product),
            Description: product.Description,
            ImageUrls: product.Images,
            Variants: product.Variants
                .Select(v => new ProductVariant(v.VariantId, v.Name, v.Price, Currency, v.Stock))
                .ToList(),
            Reviews: product.Reviews
                .Select(r => new ProductReview(r.Author, r.Rating, r.Title, r.Body, r.Date))
                .ToList(),
            Questions: product.Questions
                .Select(q => new ProductQuestion(q.Asker, q.Question, q.Answer, q.Date))
                .ToList());

        return Task.FromResult<ProductDetail?>(detail);
    }

    private static CatalogProductSummary ToSummary(CatalogProduct p) => new(
        Id: p.Id,
        Name: p.Name,
        Price: p.Price,
        Currency: Currency,
        Brand: p.Brand,
        Category: p.Category,
        ImageUrl: p.ImageUrl,
        Rating: p.Rating,
        ReviewCount: p.Reviews.Count,
        Stock: p.TotalStock);

    // Deriva las facetas (categoría / marca / rating) con conteos sobre el set
    // ya filtrado, para que la PLP refine sin recargar (faceted search).
    private static IReadOnlyList<ProductFacet> BuildFacets(IReadOnlyList<CatalogProduct> products)
    {
        if (products.Count == 0)
        {
            return Array.Empty<ProductFacet>();
        }

        var byCategory = products
            .GroupBy(p => p.Category, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ProductFacetValue(g.Key, g.Count()))
            .ToList();

        var byBrand = products
            .GroupBy(p => p.Brand, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new ProductFacetValue(g.Key, g.Count()))
            .ToList();

        // Rating buckets: "4+" y "4.5+".
        var rating = new List<ProductFacetValue>();
        var fourPlus = products.Count(p => p.Rating >= 4.0);
        var fourHalfPlus = products.Count(p => p.Rating >= 4.5);
        if (fourPlus > 0)
        {
            rating.Add(new ProductFacetValue("4.0", fourPlus));
        }
        if (fourHalfPlus > 0)
        {
            rating.Add(new ProductFacetValue("4.5", fourHalfPlus));
        }

        var facets = new List<ProductFacet>
        {
            new("category", "Categoría", byCategory),
            new("brand", "Marca", byBrand),
        };
        if (rating.Count > 0)
        {
            facets.Add(new ProductFacet("minRating", "Calificación", rating));
        }
        return facets;
    }

    // ── Modelo sembrado interno (no expuesto) ──────────────────────────

    private sealed record CatalogProduct(
        string Id,
        string Name,
        decimal Price,
        string Brand,
        string Category,
        string ImageUrl,
        double Rating,
        string Description,
        IReadOnlyList<string> Images,
        IReadOnlyList<CatalogVariant> Variants,
        IReadOnlyList<CatalogReview> Reviews,
        IReadOnlyList<CatalogQuestion> Questions)
    {
        /// <summary>Stock del producto sin variantes (override para productos planos).</summary>
        public int Stock { get; init; }

        /// <summary>Stock total: suma de variantes, o <see cref="Stock"/> si no hay variantes.</summary>
        public int TotalStock => Variants.Count > 0 ? Variants.Sum(v => v.Stock) : Stock;
    }

    private readonly record struct CatalogVariant(string VariantId, string Name, decimal Price, int Stock);

    private readonly record struct CatalogReview(string Author, int Rating, string Title, string Body, DateOnly Date);

    private readonly record struct CatalogQuestion(string Asker, string Question, string? Answer, DateOnly Date);
}
