namespace Synergos.CMS.Interfaces;

/// <summary>
/// Una búsqueda guardada por un usuario (doc propiedades-app-spec §4 — cuenta:
/// búsquedas guardadas + alertas). Es una <see cref="PropertyQuery"/> con nombre + id
/// estable + cuándo se guardó, para poder re-ejecutarla y avisar de nuevos matches.
/// </summary>
public sealed record SavedSearch(
    string Id,
    string Owner,
    string Label,
    PropertyQuery Criteria,
    DateTimeOffset SavedAt);

/// <summary>
/// Resultado de evaluar los matches de una búsqueda guardada (la "alerta"): cuántos
/// listados matchean hoy los criterios + los listados en sí. En la demo el conteo de
/// "nuevos" se SIMULA (los destacados / publicados después de guardar la búsqueda
/// cuentan como novedad); un adapter real diffea contra un watermark persistido.
/// </summary>
public sealed record SavedSearchMatches(
    string SearchId,
    int Count,
    IReadOnlyList<PropertyListing> Listings);

/// <summary>
/// Servicio de búsquedas guardadas + alertas del vertical Propiedades (doc §4). Es la
/// pieza que da SEMÁNTICA a las saved-searches sobre el seam GENÉRICO
/// <see cref="IUserCollection"/> (colección <c>saved-searches</c>): serializa/deserializa
/// la <see cref="PropertyQuery"/> en el itemRef opaco, le asigna un id estable, y
/// re-ejecuta los criterios contra <see cref="IPropertyCatalogProvider"/> para contar
/// nuevos matches (la "alerta").
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica el almacenamiento: COMPONE (DIP)
/// <see cref="IUserCollection"/> (persistencia de la lista por usuario) +
/// <see cref="IPropertyCatalogProvider"/> (el universo a matchear). <see cref="SaveAsync"/>
/// es idempotente por criterios (guardar la misma búsqueda dos veces devuelve la misma).
/// ADR 0075.
/// </remarks>
public interface ISavedSearchService
{
    /// <summary>
    /// Guarda una búsqueda con sus criterios y devuelve el registro (con id estable).
    /// Idempotente por criterios: re-guardar la misma búsqueda del mismo usuario
    /// devuelve la existente. Lanza <see cref="ArgumentException"/> si el usuario viene
    /// vacío.
    /// </summary>
    Task<SavedSearch> SaveAsync(string owner, PropertyQuery criteria, string? label = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve las búsquedas guardadas del usuario, de la más reciente a la más
    /// antigua. Lista vacía (no lanza) si no tiene.
    /// </summary>
    Task<IReadOnlyList<SavedSearch>> GetForOwnerAsync(string owner, CancellationToken cancellationToken = default);

    /// <summary>
    /// Re-ejecuta los criterios de la búsqueda guardada <paramref name="searchId"/> y
    /// devuelve los matches (conteo + listados) — la alerta de "nuevos inmuebles que
    /// cumplen tu búsqueda". Lanza <see cref="ArgumentException"/> si la búsqueda no
    /// existe.
    /// </summary>
    Task<SavedSearchMatches> GetMatchesAsync(string searchId, CancellationToken cancellationToken = default);
}
