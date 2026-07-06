namespace Synergos.CMS.Interfaces;

/// <summary>
/// Criterios de búsqueda facetada del portal inmobiliario (cara comprador/
/// arrendatario). Es el corazón del search: todos los filtros son OPCIONALES
/// (null = sin filtro) y se combinan en AND. Lo arma la barra de filtros del
/// módulo Angular <c>module-realty-portal</c> (doc propiedades-app-spec §2).
/// </summary>
public sealed record PropertyQuery(
    string? Text = null,
    string? Type = null,
    decimal? MinPrice = null,
    decimal? MaxPrice = null,
    int? Beds = null,
    string? Location = null);

/// <summary>
/// Resumen de un listado para la grilla de resultados + el pin del mapa (lo que
/// la card del portal lista). El detalle (galería + specs + ubicación exacta) se
/// resuelve aparte con <see cref="IPropertyCatalogProvider.GetListingAsync"/>.
/// <see cref="Lat"/>/<see cref="Lng"/> alimentan el mapa (sync lista↔pin).
/// </summary>
public sealed record PropertyListing(
    string Id,
    string Slug,
    string Title,
    string Operation,
    string Type,
    decimal Price,
    string Currency,
    string City,
    string Neighborhood,
    int Beds,
    int Baths,
    int AreaM2,
    int Stratum,
    double Lat,
    double Lng,
    string ImageUrl,
    bool Featured = false);

/// <summary>
/// Una faceta de búsqueda con sus valores y conteos (la "cara izquierda" de los
/// filtros: cuántos listados hay por tipo / ciudad / barrio / nº de habitaciones).
/// Se deriva del universo de listados que matchea el resto de los criterios.
/// </summary>
public sealed record PropertyFacet(string Name, IReadOnlyList<PropertyFacetValue> Values);

/// <summary>Un valor de faceta con su conteo (e.g. <c>"apartamento" → 12</c>).</summary>
public sealed record PropertyFacetValue(string Value, int Count);

/// <summary>
/// Resultado del search: los listados que matchean + las facetas derivadas
/// (conteos por tipo/ciudad/etc. del universo filtrado) para pintar la barra de
/// filtros con números reales.
/// </summary>
public sealed record PropertySearchResult(
    IReadOnlyList<PropertyListing> Listings,
    IReadOnlyList<PropertyFacet> Facets);

/// <summary>
/// Ubicación geográfica de un listado (para el mapa de la ficha + el barrio).
/// </summary>
public sealed record PropertyLocation(
    double Lat,
    double Lng,
    string Address,
    string Neighborhood,
    string City);

/// <summary>
/// Ficha completa de un listado: el resumen + specs key-value (área/hab/baños/
/// estrato/parqueaderos/antigüedad), descripción, amenities, galería de fotos y
/// la ubicación exacta para el mapa. Es lo que la PDP inmobiliaria renderiza.
/// </summary>
public sealed record PropertyDetail(
    PropertyListing Summary,
    string Description,
    IReadOnlyList<PropertySpec> Specs,
    IReadOnlyList<string> Amenities,
    IReadOnlyList<string> Gallery,
    PropertyLocation Location,
    string AgentName,
    string AgentPhone);

/// <summary>
/// Un par etiqueta-valor de la ficha (specs CO-first: "Área", "Habitaciones",
/// "Baños", "Estrato", "Parqueaderos", "Antigüedad", "Piso").
/// </summary>
public sealed record PropertySpec(string Label, string Value);

/// <summary>Coordenada geográfica de un inmueble a publicar (pin en el mapa, wizard SH-6).</summary>
public sealed record PropertyGeo(double Lat, double Lng);

/// <summary>
/// Borrador de un inmueble que el agente publica desde la consola (doc
/// propiedades-app-spec §7 — wizard de publicación SH-6). Al publicarlo aparece en
/// <see cref="IPropertyCatalogProvider.SearchAsync"/>. El precio debe ser mayor a
/// cero; el resto de campos son metadatos de la ficha. La galería son URLs de las
/// fotos ya subidas (el media-uploader del wizard resuelve el almacenamiento).
/// </summary>
public sealed record PropertyDraft(
    string Title,
    string Type,
    string Operation,
    decimal Price,
    int Beds,
    int Baths,
    int Area,
    string City,
    PropertyGeo Geo,
    IReadOnlyList<string> Gallery,
    string Description,
    string? Neighborhood = null,
    int Stratum = 0,
    string? Currency = null,
    string? AgentName = null,
    string? AgentPhone = null);

/// <summary>
/// Catálogo de listados del vertical Propiedades. Es la pieza del MOTOR que
/// resuelve "qué propiedades hay" (búsqueda facetada) + "el detalle de esta
/// propiedad": <see cref="SearchAsync"/> → listados + facetas filtrados por
/// <see cref="PropertyQuery"/>; <see cref="GetListingAsync"/> → ficha (specs +
/// galería + ubicación) o null si no existe.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IEventCatalogProvider"/> /
/// <see cref="IProductCatalogProvider"/>): el default
/// <c>StubPropertyCatalogProvider</c> (Application, lógica pura/determinista)
/// sirve un catálogo sembrado en memoria con geo (varias ciudades CO ×
/// tipos apto/casa/oficina) para que la demo corra end-to-end; el adapter real
/// (Examine sobre <c>propertyListing</c>, o una API MLS/portal) se enchufa
/// después sin tocar el motor. ADR 0002 (Application sin Umbraco).
/// </remarks>
public interface IPropertyCatalogProvider
{
    /// <summary>
    /// Devuelve los listados que matchean <paramref name="query"/> (todos los
    /// filtros en AND; null = sin filtro) + las facetas derivadas del universo
    /// filtrado. Sin criterios, devuelve todos, destacados primero.
    /// </summary>
    Task<PropertySearchResult> SearchAsync(PropertyQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la ficha del listado por id o slug, o null si no existe.
    /// </summary>
    Task<PropertyDetail?> GetListingAsync(string listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publica un inmueble nuevo (creado por un agente) en el catálogo: desde ese
    /// momento aparece en <see cref="SearchAsync"/> y su ficha se resuelve en
    /// <see cref="GetListingAsync"/>. Devuelve el detalle publicado (con su id/slug ya
    /// asignados). Lanza <see cref="ArgumentException"/> si el borrador es inválido
    /// (título vacío o precio ≤ 0). El adapter real persiste en el índice/CMS; el stub
    /// lo agrega al catálogo en memoria. Es la seam que consume el wizard de publicación
    /// de la consola del agente (DIP — la cara de agente NO conoce el almacenamiento).
    /// </summary>
    Task<PropertyDetail> PublishListingAsync(PropertyDraft draft, CancellationToken cancellationToken = default);
}
