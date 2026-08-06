using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="ICatalogSocialProof"/> durable sobre <see cref="IJsonEntityStore"/>
/// (resourceType <c>"reviews"</c>) — T10, doc <c>26</c>. ADR 0105: el store genérico es el
/// único; aquí NO se crea uno dedicado.
/// </summary>
/// <remarks>
/// <para>
/// <b>Una entrada por SKU, con sus reseñas dentro.</b> La alternativa (una entrada por reseña)
/// obligaría a listar y filtrar el universo entero para pintar una tarjeta. Agrupando por SKU,
/// leer un producto es UNA lectura, y la idempotencia por autor sale de reemplazar dentro de la
/// lista en vez de tener que buscar duplicados repartidos.
/// </para>
/// <para>
/// La clave es el SKU y llega desde una ruta (<c>products/sku/{sku}</c>).
/// <see cref="IJsonEntityStore"/> sanea la clave antes de tocar el disco (caracteres inválidos
/// de nombre de fichero y <c>".."</c>), así que el recorrido de directorios está cortado en la
/// capa que posee el path. Aquí solo se exige que el SKU no venga vacío.
/// </para>
/// </remarks>
public sealed class FileSystemCatalogSocialProof : ICatalogSocialProof
{
    private const string ResourceType = "reviews";

    private readonly IJsonEntityStore _store;
    private readonly ILogger<FileSystemCatalogSocialProof> _logger;

    public FileSystemCatalogSocialProof(
        IJsonEntityStore store,
        ILogger<FileSystemCatalogSocialProof> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<ProductSocialProof?> GetAsync(string sku, CancellationToken cancellationToken = default)
    {
        var reviews = await GetReviewsAsync(sku, cancellationToken).ConfigureAwait(false);
        if (reviews.Count == 0)
        {
            // AUSENCIA, no cero (ADR 0112). Quien pinte esto no debe recibir nunca un
            // agregado "0,0" que parezca un producto valorado con la peor nota.
            return null;
        }

        // Una decimal: `4.333333…` en el JSON no es más verdad, es ruido — y la tarjeta
        // pinta una sola cifra.
        var average = Math.Round(reviews.Average(r => (double)r.Rating), 1, MidpointRounding.AwayFromZero);
        return new ProductSocialProof(sku, average, reviews.Count);
    }

    public async Task<IReadOnlyList<CustomerReview>> GetReviewsAsync(
        string sku,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            return Array.Empty<CustomerReview>();
        }

        var json = await _store.ReadAsync(ResourceType, sku, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Array.Empty<CustomerReview>();
        }

        var stored = Deserialize(json, sku);
        return stored
            .OrderByDescending(r => r.Date)
            .ToList();
    }

    public async Task UpsertReviewAsync(CustomerReview review, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);

        if (string.IsNullOrWhiteSpace(review.Sku))
        {
            throw new ArgumentException("La reseña requiere el SKU del producto.", nameof(review));
        }
        if (review.AuthorKey == Guid.Empty)
        {
            // Sin autor no hay idempotencia posible (cada envío sería una reseña nueva) ni
            // forma de comprobar que compró. Es un fallo de programación, no del usuario.
            throw new ArgumentException("La reseña requiere el memberKey del autor.", nameof(review));
        }
        if (review.Rating is < 1 or > 5)
        {
            // Recortar en silencio convertiría un bug del llamante en un 5 estrellas.
            throw new ArgumentOutOfRangeException(
                nameof(review), review.Rating, "El rating de una reseña va de 1 a 5.");
        }

        var current = await GetReviewsAsync(review.Sku, cancellationToken).ConfigureAwait(false);
        var next = current
            .Where(r => r.AuthorKey != review.AuthorKey)   // idempotencia: el autor sustituye la suya
            .Append(review)
            .ToList();

        await _store
            .WriteAsync(ResourceType, review.Sku, JsonSerializer.Serialize(next), cancellationToken)
            .ConfigureAwait(false);
    }

    private IReadOnlyList<CustomerReview> Deserialize(string json, string sku)
    {
        try
        {
            return JsonSerializer.Deserialize<List<CustomerReview>>(json) ?? new List<CustomerReview>();
        }
        catch (JsonException ex)
        {
            // Un fichero corrupto degrada a "sin reseñas": la tarjeta se queda sin estrella,
            // que es el estado honesto. Tumbar el catálogo entero por esto sería peor.
            _logger.LogWarning(ex, "FileSystemCatalogSocialProof: reseñas ilegibles para {Sku}", sku);
            return Array.Empty<CustomerReview>();
        }
    }
}
