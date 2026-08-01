using System.Globalization;
using System.Text;
using System.Text.Json;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El portal inmobiliario servido desde el CONTENIDO que el editor autoró. Es el
/// <see cref="IPropertyCatalogProvider"/> que se registra cuando
/// <c>Synergos:Catalog:Sources:Realty = cms</c>.
/// </summary>
/// <remarks>
/// <b>Misma forma de dos capas que <see cref="CatalogEventCatalogProvider"/></b>, y por las
/// mismas razones: debajo el contenido (<see cref="ICatalogSource{T}"/> de
/// <see cref="PropertyDetail"/>, en producción <c>UmbracoPropertyCatalogSource</c>); encima,
/// lo que un AGENTE publicó desde la consola, persistido en el store genérico (ADR 0105). La
/// capa de arriba gana: publicar y no ver el cambio es peor que la ambigüedad.
///
/// <para><b>La búsqueda se calca del stub, no se reinventa.</b> Mismo descriptor, mismo motor,
/// mismo pre-filtro de precio y ubicación fuera del índice. Si el buscador se comportara
/// distinto según de dónde salen los inmuebles, sería una regresión invisible que solo
/// aparecería al mover el flag a <c>cms</c> — y en un portal inmobiliario el buscador ES el
/// producto.</para>
///
/// <para>Lógica pura: cero <c>Umbraco.Cms.*</c> y cero <c>Microsoft.AspNetCore.*</c>
/// (ADR 0002).</para>
/// </remarks>
public sealed class CatalogPropertyCatalogProvider : IPropertyCatalogProvider
{
    /// <summary>Familia de entidades del store para los inmuebles que publica el agente.</summary>
    public const string ResourceType = "realty-catalog";

    private const string GeneratedIdPrefix = "prop-org-";
    private const int MaxKeyLength = 96;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly ICatalogSource<PropertyDetail> _source;
    private readonly IJsonEntityStore _store;
    private readonly ICatalogIndex<PropertyListing> _index;
    private readonly string _resourceType;

    public CatalogPropertyCatalogProvider(ICatalogSource<PropertyDetail> source, IJsonEntityStore store)
        : this(source, store, ResourceType, new InMemoryCatalogIndex<PropertyListing>(
            StubPropertyCatalogProvider.Descriptor, CatalogSettings.Unpaged))
    {
    }

    internal CatalogPropertyCatalogProvider(
        ICatalogSource<PropertyDetail> source,
        IJsonEntityStore store,
        string resourceType,
        ICatalogIndex<PropertyListing> index)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _index = index ?? throw new ArgumentNullException(nameof(index));
        _resourceType = string.IsNullOrWhiteSpace(resourceType)
            ? throw new ArgumentException("El resourceType del store no puede ir vacío.", nameof(resourceType))
            : resourceType;
    }

    public async Task<PropertySearchResult> SearchAsync(
        PropertyQuery query,
        CancellationToken cancellationToken = default)
    {
        query ??= new PropertyQuery();

        var catalog = await LoadCatalogAsync(cancellationToken).ConfigureAwait(false);

        // Precio y ubicación se acotan AQUÍ y no en el descriptor, calcando al stub: el rango
        // de precio es continuo (el usuario lo teclea) y no los tramos declarados que espera
        // un CatalogRangeFilter; y `location` busca por SUBSTRING sobre ciudad O barrio
        // ("laureles" es un barrio, "medell" media ciudad), mientras una faceta casa por
        // igualdad exacta. Doblarlo en la faceta `city` haría dos daños: buscar un barrio
        // devolvería cero, y los barrios saldrían como chips en la columna Ciudad.
        var universe = catalog
            .Select(d => d.Summary)
            .Where(l => query.MinPrice is null || l.Price >= query.MinPrice.Value)
            .Where(l => query.MaxPrice is null || l.Price <= query.MaxPrice.Value)
            .Where(l => MatchesLocation(l, query.Location))
            .ToList();

        var filters = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            filters["type"] = new[] { query.Type.Trim() };
        }

        if (query.Beds is > 0)
        {
            filters["beds"] = new[] { query.Beds.Value.ToString(CultureInfo.InvariantCulture) };
        }

        var result = _index.Search(universe, new CatalogQuery(
            Text: query.Text,
            Filters: filters.Count > 0 ? filters : null,
            // Take explícito: esta seam promete TODAS las coincidencias, y el default del
            // motor (24) las truncaría en silencio.
            Take: int.MaxValue));

        return new PropertySearchResult(
            Listings: result.Items,
            Facets: result.Facets
                .Select(f => new PropertyFacet(
                    f.Field,
                    f.Values.Select(v => new PropertyFacetValue(v.Value, v.Count, v.Label)).ToList(),
                    f.Kind.ToString()))
                .ToList());
    }

    public async Task<PropertyDetail?> GetListingAsync(
        string listingId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            return null;
        }

        var id = listingId.Trim();

        var published = await ReadPublishedAsync(cancellationToken).ConfigureAwait(false);
        var overlayMatch = published.FirstOrDefault(d => IsMatch(d, id));
        if (overlayMatch is not null)
        {
            return overlayMatch;
        }

        var content = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);
        return content.FirstOrDefault(d => IsMatch(d, id));
    }

    /// <summary>
    /// Publica el inmueble del agente. Las validaciones son las MISMAS que las del stub.
    /// </summary>
    /// <remarks>
    /// Se lanza —en vez de omitir, como hace la proyección de contenido— porque aquí el
    /// llamador es un formulario, no un editor de backoffice: un 400 con la razón se puede
    /// pintar en el campo que está mal, y un inmueble que "se publicó" y no aparece no.
    /// </remarks>
    public async Task<PropertyDetail> PublishListingAsync(
        PropertyDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Title))
        {
            throw new ArgumentException("El título del inmueble es obligatorio.", nameof(draft));
        }

        if (draft.Price <= 0m)
        {
            throw new ArgumentException("El precio del inmueble debe ser mayor a cero.", nameof(draft));
        }

        if (draft.Geo is null)
        {
            throw new ArgumentException("La ubicación (geo) del inmueble es obligatoria.", nameof(draft));
        }

        var detail = BuildFromDraft(draft);
        var json = JsonSerializer.Serialize(detail, JsonOptions);
        await _store.WriteAsync(_resourceType, detail.Summary.Id, json, cancellationToken).ConfigureAwait(false);

        return detail;
    }

    /// <summary>
    /// El borrador del agente convertido en ficha, con el slug como identidad.
    /// </summary>
    /// <remarks>
    /// El id sale del slug del título, igual que en el catálogo de contenido, para que
    /// re-publicar un inmueble lo SOBREESCRIBA en vez de duplicarlo. Cuando el título no deja
    /// nada utilizable (solo símbolos, por ejemplo) se acuña uno con sufijo aleatorio y no con
    /// un contador: el contador vivía en el proceso, y este catálogo es durable — tras un
    /// reinicio volvería a 1 y el segundo inmueble publicado pisaría al primero.
    /// </remarks>
    private static PropertyDetail BuildFromDraft(PropertyDraft draft)
    {
        var slug = Sanitize(draft.Title);
        if (slug.Length == 0)
        {
            slug = GeneratedIdPrefix + Guid.NewGuid().ToString("n")[..12];
        }

        var gallery = (draft.Gallery ?? Array.Empty<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g.Trim())
            .ToList();

        var city = draft.City?.Trim() ?? string.Empty;
        var neighborhood = draft.Neighborhood?.Trim() ?? string.Empty;
        var currency = string.IsNullOrWhiteSpace(draft.Currency) ? "COP" : draft.Currency.Trim();

        var summary = new PropertyListing(
            Id: slug,
            Slug: slug,
            Title: draft.Title.Trim(),
            Operation: string.IsNullOrWhiteSpace(draft.Operation) ? "venta" : draft.Operation.Trim().ToLowerInvariant(),
            Type: string.IsNullOrWhiteSpace(draft.Type) ? "apartamento" : draft.Type.Trim().ToLowerInvariant(),
            Price: draft.Price,
            Currency: currency,
            City: city,
            Neighborhood: neighborhood,
            Beds: Math.Max(0, draft.Beds),
            Baths: Math.Max(0, draft.Baths),
            AreaM2: Math.Max(0, draft.Area),
            Stratum: Math.Max(0, draft.Stratum),
            Lat: draft.Geo.Lat,
            Lng: draft.Geo.Lng,
            ImageUrl: gallery.Count > 0 ? gallery[0] : string.Empty,
            Featured: false);

        var address = neighborhood.Length > 0 && city.Length > 0
            ? $"{neighborhood}, {city}"
            : neighborhood.Length > 0 ? neighborhood : city;

        return new PropertyDetail(
            Summary: summary,
            Description: draft.Description?.Trim() ?? string.Empty,
            // Las características quedan vacías a propósito: el wizard del agente captura
            // habitaciones/baños/área, que YA viajan en el resumen y los pinta la tarjeta.
            // Duplicarlas aquí con etiquetas inventadas en el servidor —sin pasar por el
            // Dictionary, que esta capa no ve— daría dos listas distintas para lo mismo.
            Specs: Array.Empty<PropertySpec>(),
            Amenities: Array.Empty<string>(),
            Gallery: gallery,
            Location: new PropertyLocation(draft.Geo.Lat, draft.Geo.Lng, address, neighborhood, city),
            AgentName: string.IsNullOrWhiteSpace(draft.AgentName) ? string.Empty : draft.AgentName.Trim(),
            AgentPhone: string.IsNullOrWhiteSpace(draft.AgentPhone) ? string.Empty : draft.AgentPhone.Trim());
    }

    private static bool MatchesLocation(PropertyListing listing, string? location)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return true;
        }

        var loc = location.Trim();
        return CatalogText.Contains(listing.City, loc)
            || CatalogText.Contains(listing.Neighborhood, loc);
    }

    private static bool IsMatch(PropertyDetail detail, string id)
        => string.Equals(detail.Summary.Id, id, StringComparison.OrdinalIgnoreCase)
            || string.Equals(detail.Summary.Slug, id, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Las dos capas fusionadas, con lo publicado por el agente ganando por id o por slug.
    /// </summary>
    private async Task<IReadOnlyList<PropertyDetail>> LoadCatalogAsync(CancellationToken cancellationToken)
    {
        var published = await ReadPublishedAsync(cancellationToken).ConfigureAwait(false);
        var content = await _source.GetAllAsync(null, cancellationToken).ConfigureAwait(false);

        if (published.Count == 0)
        {
            return content;
        }

        var shadowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in published)
        {
            shadowed.Add(d.Summary.Id);
            if (!string.IsNullOrWhiteSpace(d.Summary.Slug))
            {
                shadowed.Add(d.Summary.Slug);
            }
        }

        var merged = new List<PropertyDetail>(published);
        merged.AddRange(content.Where(d =>
            !shadowed.Contains(d.Summary.Id)
            && (string.IsNullOrWhiteSpace(d.Summary.Slug) || !shadowed.Contains(d.Summary.Slug))));

        return merged;
    }

    /// <summary>
    /// Los inmuebles publicados por agentes. Una entrada ilegible se SALTA.
    /// </summary>
    /// <remarks>
    /// Un JSON corrupto —un despliegue a medias, una edición a mano— no puede tumbar el portal
    /// entero: se pierde ese inmueble, no el catálogo.
    /// </remarks>
    private async Task<IReadOnlyList<PropertyDetail>> ReadPublishedAsync(CancellationToken cancellationToken)
    {
        // ListAsync devuelve los DOCUMENTOS, no las claves — es la forma de esta seam.
        var documents = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        if (documents.Count == 0)
        {
            return Array.Empty<PropertyDetail>();
        }

        var listings = new List<PropertyDetail>(documents.Count);
        foreach (var json in documents)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            PropertyDetail? detail;
            try
            {
                detail = JsonSerializer.Deserialize<PropertyDetail>(json, JsonOptions);
            }
            catch (JsonException)
            {
                continue;
            }

            if (detail?.Summary is not null && !string.IsNullOrWhiteSpace(detail.Summary.Id))
            {
                listings.Add(detail);
            }
        }

        return listings;
    }

    /// <summary>
    /// Un id apto para ser clave del store: minúsculas, dígitos y guiones, nada más.
    /// </summary>
    /// <remarks>
    /// La clave termina siendo un nombre de archivo en el adapter FileSystem, así que un id con
    /// <c>../</c> no es un id feo: es una escritura fuera del directorio del recurso. Lista
    /// blanca y no negra, porque una lista negra siempre se queda corta.
    /// </remarks>
    private static string Sanitize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(Math.Min(raw.Length, MaxKeyLength));
        foreach (var c in raw.Trim().ToLowerInvariant())
        {
            if (builder.Length == MaxKeyLength)
            {
                break;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }
}
