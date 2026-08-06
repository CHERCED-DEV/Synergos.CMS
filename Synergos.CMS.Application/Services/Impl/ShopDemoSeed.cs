using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El catálogo de demo de la Tienda: los datos, y nada más. Sale del provider para que éste
/// quede como una fachada sobre el motor transversal (<see cref="ICatalogIndex{T}"/>).
/// </summary>
/// <remarks>
/// <b>Estos productos NO son los que el editor autoró.</b> Hoy la mercancía comprable está
/// aquí, hardcodeada, y la que se autora en el CMS (<c>TSHIRT-NEGRA-001</c>) está publicada,
/// viva e INERTE: los dos universos no comparten un solo SKU. Todo T1/T2/T3 —persistencia,
/// ownership, pagos durables, webhook— se construyó sobre mercancía que no existe como
/// contenido. Eso lo cierra la Ola A cambiando qué <see cref="ICatalogSource{T}"/> se
/// registra; este seed NO se borra entonces: queda como el fallback de la demo, detrás de
/// <c>Synergos:Catalog:Sources:Shop = demo|cms</c> (calco del gating de T3/ADR 0104).
/// </remarks>
internal static class ShopDemoSeed
{
    internal const string Currency = "COP";

    // Catálogo sembrado: 6 productos en 3 categorías (Tecnología / Hogar /
    // Deportes), con marcas, rating, stock, variantes, reviews y Q&A.
    internal static readonly IReadOnlyList<CatalogProduct> Products = new[]
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
            Description: "Tenis de running con espuma de retorno de energía, malla transpirable y suela de alta tracción.",
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
}

// ── Modelo del seed. Antes eran records PRIVADOS anidados en el provider; suben a
//    top-level internal para que ICatalogSource<CatalogProduct> pueda nombrarlos. ──

public sealed record CatalogProduct(
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

public readonly record struct CatalogVariant(string VariantId, string Name, decimal Price, int Stock);

public readonly record struct CatalogReview(string Author, int Rating, string Title, string Body, DateOnly Date);

public readonly record struct CatalogQuestion(string Asker, string Question, string? Answer, DateOnly Date);
