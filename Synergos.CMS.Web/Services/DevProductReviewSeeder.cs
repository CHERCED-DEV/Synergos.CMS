using System.Security.Cryptography;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Siembra reseñas de DEMO sobre los productos que existen de verdad en el catálogo, para que
/// la prueba social de T10 (ADR 0114) tenga dato que enseñar. Tooling dev, tras el flag
/// <c>Synergos:DevSeed:Enabled</c> — nada de esto corre en boot (ADR 0013).
/// </summary>
/// <remarks>
/// <para>
/// <b>Siembra contra el catálogo VIVO, no contra el de demo.</b> `ShopDemoSeed` ya trae reseñas
/// escritas, y era la fuente obvia — pero sus ids son slugs (<c>tec-laptop-pro-14</c>) y el
/// catálogo real, ya mudado a contenido, usa SKUs (<c>AUDIF-DIADEMA-001</c>). Sembrar aquellas
/// habría creado entradas HUÉRFANAS: ficheros en disco que ninguna tarjeta lee, y una demo que
/// sigue sin estrella mientras todo parece hecho. Los SKUs se leen de <see cref="IShopQuery"/>
/// en el momento de sembrar.
/// </para>
/// <para>
/// <b>Es idempotente por construcción.</b> El <c>AuthorKey</c> NO es aleatorio: se deriva del
/// SKU y del índice del reseñador, así que volver a sembrar SUSTITUYE las mismas reseñas en vez
/// de acumular (la idempotencia del seam es por autor). Sin esto, tres ejecuciones dejarían el
/// conteo a 9 y la media movida — un dato falso con cara de real.
/// </para>
/// </remarks>
public sealed class DevProductReviewSeeder
{
    private readonly IShopQuery _shopQuery;
    private readonly ICatalogSocialProof _socialProof;
    private readonly ILogger<DevProductReviewSeeder> _logger;

    public DevProductReviewSeeder(
        IShopQuery shopQuery,
        ICatalogSocialProof socialProof,
        ILogger<DevProductReviewSeeder> logger)
    {
        _shopQuery = shopQuery;
        _socialProof = socialProof;
        _logger = logger;
    }

    /// <summary>Plantillas de reseña. Texto de demo, deliberadamente variado en tono y nota.</summary>
    private static readonly (string Author, int Rating, string Title, string Body)[] Templates =
    {
        ("Camila R.",   5, "Cumple lo que promete",   "Llegó antes de lo previsto y la calidad se nota al usarlo."),
        ("Andrés M.",   4, "Muy bueno, con un pero",  "Funciona de sobra para el día a día; el empaque podría ser mejor."),
        ("Valentina G.",5, "Repito sin dudar",        "Segunda vez que compro aquí y la experiencia fue igual de buena."),
        ("Felipe O.",   3, "Correcto para el precio", "Hace su trabajo, aunque esperaba un acabado un poco más fino."),
    };

    /// <summary>
    /// Siembra reseñas para los productos del catálogo. Devuelve cuántos productos quedaron
    /// con prueba social y cuántas reseñas se escribieron en total.
    /// </summary>
    public async Task<(int Products, int Reviews)> SeedAsync(int maxProducts, CancellationToken cancellationToken)
    {
        var products = _shopQuery.GetProducts(new ShopQueryRequest(MaxItems: maxProducts));
        if (products.Count == 0)
        {
            // Sin catálogo no hay nada que reseñar. Devolver (0,0) y que el llamador lo diga,
            // en vez de fingir éxito.
            _logger.LogWarning("DevProductReviewSeeder: el catálogo no devolvió productos; nada que sembrar.");
            return (0, 0);
        }

        var reviews = 0;
        foreach (var product in products)
        {
            // Cuántas reseñas lleva cada producto (1..3), estable por SKU: así el catálogo no
            // sale con todas las tarjetas idénticas y, aun así, re-sembrar da lo mismo.
            var count = 1 + (StableSeed(product.Sku) % 3);
            for (var i = 0; i < count; i++)
            {
                var template = Templates[(StableSeed(product.Sku) + i) % Templates.Length];
                await _socialProof.UpsertReviewAsync(
                    new CustomerReview(
                        Sku: product.Sku,
                        AuthorKey: StableAuthorKey(product.Sku, i),
                        AuthorName: template.Author,
                        Rating: template.Rating,
                        Title: template.Title,
                        Body: template.Body,
                        Date: new DateOnly(2026, 6, 1 + ((StableSeed(product.Sku) + i) % 28))),
                    cancellationToken).ConfigureAwait(false);
                reviews++;
            }
        }

        _logger.LogInformation(
            "DevProductReviewSeeder: {Reviews} reseñas sembradas sobre {Products} productos.",
            reviews, products.Count);
        return (products.Count, reviews);
    }

    /// <summary>
    /// Entero estable a partir del SKU. <c>string.GetHashCode()</c> NO sirve: está
    /// randomizado por proceso, así que la siembra cambiaría en cada reinicio — el mismo fallo
    /// que ya mordió al token de las entradas (ADR 0110).
    /// </summary>
    private static int StableSeed(string sku)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sku));
        return BitConverter.ToInt32(hash, 0) & 0x7FFFFFFF;
    }

    /// <summary>
    /// GUID determinista por (SKU, índice). Es lo que hace la siembra idempotente: el seam
    /// sustituye por <c>AuthorKey</c>, así que re-sembrar reescribe en vez de acumular.
    /// </summary>
    private static Guid StableAuthorKey(string sku, int index)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"dev-review:{sku}:{index}"));
        return new Guid(hash.AsSpan(0, 16));
    }
}
