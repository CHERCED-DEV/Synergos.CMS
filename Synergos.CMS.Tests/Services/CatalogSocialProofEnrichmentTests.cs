using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre el enriquecimiento con prueba social del catálogo de search/PDP (T10, ADR 0114):
/// la nota y las reseñas se pegan a <see cref="CatalogProduct"/> en la FUENTE, no al emitir
/// el DTO, para que el motor derive facetas, <c>minRating</c> y orden sobre datos reales.
/// </summary>
/// <remarks>
/// El enriquecimiento vive dentro de <c>UmbracoProductCatalogSource</c>, que necesita
/// <c>IUmbracoContext</c> y no se puede instanciar aquí. Lo que se prueba es la REGLA que
/// aplica —derivar la media, proyectar las reseñas, y dejar intacto lo que no tiene— con la
/// misma forma exacta que el código de producción, incluida la reconstrucción con <c>with</c>.
/// Es la parte que puede equivocarse; el paseo por Umbraco no.
/// </remarks>
public class CatalogSocialProofEnrichmentTests
{
    private static CatalogProduct Product(string id, double rating = 0d) => new(
        Id: id,
        Name: "Audífonos de diadema",
        Price: 320_000m,
        Brand: "Aurora",
        Category: "Tecnología",
        ImageUrl: "/media/x.jpg",
        Rating: rating,
        Description: "desc",
        Images: Array.Empty<string>(),
        Variants: Array.Empty<CatalogVariant>(),
        Reviews: Array.Empty<CatalogReview>(),
        Questions: Array.Empty<CatalogQuestion>());

    private static CustomerReview Review(string sku, int rating, string author = "Ana") =>
        new(sku, Guid.NewGuid(), author, rating, "Título", "Cuerpo", new DateOnly(2026, 7, 1));

    /// <summary>Calca la regla de <c>UmbracoProductCatalogSource.WithSocialProofAsync</c>.</summary>
    private static IReadOnlyList<CatalogProduct> Enrich(
        IReadOnlyList<CatalogProduct> products,
        IReadOnlyDictionary<string, IReadOnlyList<CustomerReview>> bySku)
        => products.Select(p =>
        {
            if (!bySku.TryGetValue(p.Id, out var reviews) || reviews.Count == 0)
            {
                return p;
            }
            return p with
            {
                Rating = Math.Round(reviews.Average(r => (double)r.Rating), 1, MidpointRounding.AwayFromZero),
                Reviews = reviews
                    .Select(r => new CatalogReview(r.AuthorName, r.Rating, r.Title, r.Body, r.Date))
                    .ToList(),
            };
        }).ToList();

    [Fact]
    public void Un_producto_sin_resenas_se_queda_EXACTAMENTE_como_estaba()
    {
        var original = Product("SKU-1");

        var result = Enrich(new[] { original }, new Dictionary<string, IReadOnlyList<CustomerReview>>());

        // Mismo objeto: no se reconstruye nada, así que no hay forma de perder un campo.
        Assert.Same(original, Assert.Single(result));
        Assert.Equal(0d, result[0].Rating);
        Assert.Empty(result[0].Reviews);
    }

    [Fact]
    public void La_nota_se_deriva_y_el_ReviewCount_sale_del_numero_de_resenas()
    {
        var bySku = new Dictionary<string, IReadOnlyList<CustomerReview>>
        {
            ["SKU-1"] = new[] { Review("SKU-1", 5), Review("SKU-1", 4), Review("SKU-1", 4) },
        };

        var enriched = Enrich(new[] { Product("SKU-1") }, bySku);

        Assert.Equal(4.3, enriched[0].Rating);          // 13/3 = 4.333… → una decimal
        Assert.Equal(3, enriched[0].Reviews.Count);     // lo que ToSummary usa como ReviewCount
    }

    [Fact]
    public void Las_resenas_se_proyectan_al_tipo_de_la_PDP_SIN_arrastrar_la_identidad()
    {
        // CustomerReview lleva AuthorKey (el memberKey). CatalogReview NO tiene dónde
        // ponerlo, y eso es la garantía de que la identidad no viaja a la vista.
        var review = Review("SKU-1", 5, author: "Ana Torres");
        var bySku = new Dictionary<string, IReadOnlyList<CustomerReview>> { ["SKU-1"] = new[] { review } };

        var enriched = Enrich(new[] { Product("SKU-1") }, bySku);

        var projected = Assert.Single(enriched[0].Reviews);
        Assert.Equal("Ana Torres", projected.Author);
        Assert.Equal(5, projected.Rating);
        Assert.DoesNotContain(
            review.AuthorKey.ToString(),
            $"{projected.Author}{projected.Title}{projected.Body}",
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Enriquecer_NO_pisa_el_resto_de_campos_del_producto()
    {
        // El control de `with` frente a reconstruir por posición: si alguien cambiara esto
        // por `new CatalogProduct(...)`, los campos no listados se perderían EN SILENCIO.
        var original = Product("SKU-1");
        var bySku = new Dictionary<string, IReadOnlyList<CustomerReview>> { ["SKU-1"] = new[] { Review("SKU-1", 5) } };

        var enriched = Enrich(new[] { original }, bySku)[0];

        Assert.Equal(original.Name, enriched.Name);
        Assert.Equal(original.Price, enriched.Price);
        Assert.Equal(original.Brand, enriched.Brand);
        Assert.Equal(original.Category, enriched.Category);
        Assert.Equal(original.ImageUrl, enriched.ImageUrl);
        Assert.Equal(original.Description, enriched.Description);
    }

    [Fact]
    public void Cada_producto_recibe_SUS_resenas_y_no_las_del_vecino()
    {
        var bySku = new Dictionary<string, IReadOnlyList<CustomerReview>>
        {
            ["SKU-1"] = new[] { Review("SKU-1", 5) },
            ["SKU-2"] = new[] { Review("SKU-2", 2), Review("SKU-2", 2) },
        };

        var enriched = Enrich(new[] { Product("SKU-1"), Product("SKU-2") }, bySku);

        Assert.Equal(5d, enriched[0].Rating);
        Assert.Equal(1, enriched[0].Reviews.Count);
        Assert.Equal(2d, enriched[1].Rating);
        Assert.Equal(2, enriched[1].Reviews.Count);
    }
}
