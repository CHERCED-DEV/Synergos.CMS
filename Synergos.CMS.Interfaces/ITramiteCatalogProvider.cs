namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resumen de un trámite para el catálogo (cara de ciudadano). Es la unidad que la
/// pantalla de catálogo lista para la búsqueda facetada; la ficha completa
/// (<see cref="TramiteDetail"/> con form definition + requisitos + tasa) se resuelve
/// aparte con <see cref="ITramiteCatalogProvider.GetAsync"/>.
/// </summary>
public sealed record TramiteSummary(
    string Id,
    string Slug,
    string Name,
    string Entity,
    string Category,
    string Channel,
    string EstimatedTime,
    decimal Fee,
    string Currency);

/// <summary>
/// Un campo del formulario dinámico (data-driven) de un trámite. El wizard de
/// radicación se renderiza desde la lista de campos (patrón GOV.UK task-list): cada
/// trámite varía su formulario sin tocar el módulo Angular. <see cref="Type"/>:
/// <c>text | textarea | number | date | email | select | checkbox</c>;
/// <see cref="Options"/> aplica solo a <c>select</c>.
/// </summary>
public sealed record TramiteFormField(
    string Key,
    string Label,
    string Type,
    bool Required,
    IReadOnlyList<string> Options);

/// <summary>
/// Definición del formulario dinámico de un trámite — la lista ordenada de campos
/// que dirige el wizard de radicación. Data-driven: el renderer recorre
/// <see cref="Fields"/> sin conocer el trámite concreto.
/// </summary>
public sealed record TramiteFormDefinition(IReadOnlyList<TramiteFormField> Fields);

/// <summary>
/// Ficha completa de un trámite: el resumen + lenguaje claro (qué es / quién puede) +
/// la <see cref="FormDefinition"/> (campos data-driven) + los requisitos/documentos +
/// la <see cref="Fee"/> (0 = gratis). Es lo que la pantalla de ficha renderiza y desde
/// donde el ciudadano hace "Iniciar trámite".
/// </summary>
public sealed record TramiteDetail(
    TramiteSummary Summary,
    string Description,
    string EligibilityText,
    string Normativa,
    TramiteFormDefinition FormDefinition,
    IReadOnlyList<string> Requirements,
    decimal Fee,
    string Currency,
    bool RequiresAppointment);

/// <summary>
/// Catálogo de trámites del vertical Gobierno (doc gobierno-app-spec). Es la pieza
/// del MOTOR que resuelve "qué trámites hay" + "la ficha de este trámite":
/// <see cref="SearchAsync"/> → lista de <see cref="TramiteSummary"/> (filtrada por
/// texto + categoría); <see cref="GetAsync"/> → <see cref="TramiteDetail"/> (form
/// definition + requisitos + tasa) o null si no existe.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IEventCatalogProvider"/> /
/// <see cref="IPropertyCatalogProvider"/>): el default
/// <c>StubTramiteCatalogProvider</c> (Application, lógica pura) sirve un catálogo
/// sembrado en memoria (varios trámites × form definition × tasa) para que la demo
/// corra end-to-end; el adapter real (SUIT / catálogo de la entidad vía Content
/// Delivery API) se enchufa después sin tocar el motor. ADR 0002 (Application sin
/// Umbraco).
/// </remarks>
public interface ITramiteCatalogProvider
{
    /// <summary>
    /// Devuelve los trámites del catálogo que matchean el texto libre
    /// <paramref name="query"/> (nombre / entidad / categoría) filtrados por
    /// <paramref name="category"/> (null/vacío = todas). Si el query es null/vacío,
    /// devuelve todos, ordenados por nombre ascendente.
    /// </summary>
    Task<IReadOnlyList<TramiteSummary>> SearchAsync(string? query, string? category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la ficha del trámite por id o slug, o null si no existe.
    /// </summary>
    Task<TramiteDetail?> GetAsync(string tramiteId, CancellationToken cancellationToken = default);
}
