namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resumen de un trámite para el catálogo (cara de ciudadano). Es la unidad que la
/// pantalla de catálogo lista para la búsqueda facetada; la ficha completa
/// (<see cref="TramiteDetail"/> con pasos + elegibilidad + requisitos + tasa) se
/// resuelve aparte con <see cref="ITramiteCatalogProvider.GetAsync"/>. La tasa viaja en
/// <see cref="FeeMinor"/> (unidades menores de la moneda — COP no tiene centavos, así
/// que coincide con el monto entero).
/// </summary>
public sealed record TramiteSummary(
    string Id,
    string Slug,
    string Name,
    string Summary,
    string Category,
    string Agency,
    string Channel,
    int EstimatedDays,
    decimal FeeMinor,
    string Currency);

/// <summary>
/// Una opción de un campo <c>select</c>/<c>radio</c> del formulario dinámico — el par
/// valor↔etiqueta que la UI renderiza sin conocer el trámite concreto.
/// </summary>
public sealed record TramiteFieldOption(string Value, string Label);

/// <summary>
/// Un campo del formulario dinámico (data-driven) de un trámite. El wizard de
/// radicación se renderiza desde las secciones (patrón GOV.UK task-list): cada trámite
/// varía su formulario sin tocar el módulo Angular. <see cref="Type"/>:
/// <c>text | textarea | number | date | select | radio | checkbox | file | email |
/// tel</c>. <see cref="Options"/> aplica a <c>select</c>/<c>radio</c>;
/// <see cref="Pattern"/> es una regex de validación opcional; <see cref="Help"/> es
/// texto de ayuda editor-facing.
/// </summary>
public sealed record TramiteFormField(
    string Id,
    string Label,
    string Type,
    bool Required,
    string Help,
    IReadOnlyList<TramiteFieldOption> Options,
    string? Pattern = null);

/// <summary>
/// Una sección del formulario dinámico — un grupo de campos con título (patrón GOV.UK
/// "una tarea por sección"). El renderer recorre <see cref="Sections"/> → <see cref="Fields"/>
/// en orden sin conocer el trámite concreto.
/// </summary>
public sealed record TramiteFormSection(
    string Id,
    string Title,
    IReadOnlyList<TramiteFormField> Fields);

/// <summary>
/// Definición del formulario dinámico de un trámite — el título + las secciones
/// ordenadas que dirigen el wizard de radicación. Data-driven: el renderer recorre las
/// secciones sin conocer el trámite concreto.
/// </summary>
public sealed record TramiteFormDefinition(
    string Id,
    string Title,
    IReadOnlyList<TramiteFormSection> Sections);

/// <summary>
/// Un paso del trámite (lenguaje claro, cara ciudadano): el recorrido "qué pasa" que la
/// ficha muestra antes de radicar (radicar → revisión → resolución).
/// </summary>
public sealed record TramiteStep(string Id, string Title, string Detail);

/// <summary>
/// Ficha completa de un trámite: el resumen + lenguaje claro (descripción) + los
/// <see cref="Steps"/> (qué pasa) + <see cref="Eligibility"/> (quién puede) +
/// <see cref="Required"/> (documentos a adjuntar) + la <see cref="FormDefinition"/>
/// (formulario dinámico seccionado) + la tasa (<see cref="FeeMinor"/> = 0 → gratis). Es
/// lo que la pantalla de ficha renderiza y desde donde el ciudadano hace "Iniciar
/// trámite".
/// </summary>
public sealed record TramiteDetail(
    TramiteSummary Summary,
    string Description,
    IReadOnlyList<TramiteStep> Steps,
    IReadOnlyList<string> Eligibility,
    IReadOnlyList<string> Required,
    string Normativa,
    TramiteFormDefinition FormDefinition,
    bool RequiresAppointment);

/// <summary>
/// Catálogo de trámites del vertical Gobierno (doc gobierno.md). Es la pieza del MOTOR
/// que resuelve "qué trámites hay" + "la ficha de este trámite":
/// <see cref="SearchAsync"/> → lista de <see cref="TramiteSummary"/> (filtrada por
/// texto + categoría); <see cref="GetAsync"/> → <see cref="TramiteDetail"/> (pasos +
/// elegibilidad + requisitos + formulario dinámico) o null si no existe.
/// </summary>
/// <remarks>
/// Seam stub-first (igual que <see cref="IEventCatalogProvider"/> /
/// <see cref="IPropertyCatalogProvider"/>): el default
/// <c>StubTramiteCatalogProvider</c> (Application, lógica pura) sirve un catálogo
/// sembrado en memoria (varios trámites × formulario seccionado × tasa) para que la demo
/// corra end-to-end; el adapter real (SUIT / catálogo de la entidad vía Content
/// Delivery API) se enchufa después sin tocar el motor. ADR 0002 (Application sin
/// Umbraco).
/// </remarks>
public interface ITramiteCatalogProvider
{
    /// <summary>
    /// Devuelve los trámites del catálogo que matchean el texto libre
    /// <paramref name="query"/> (nombre / entidad / categoría / resumen) filtrados por
    /// <paramref name="category"/> (null/vacío = todas). Si el query es null/vacío,
    /// devuelve todos, ordenados por nombre ascendente.
    /// </summary>
    Task<IReadOnlyList<TramiteSummary>> SearchAsync(string? query, string? category, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve la ficha del trámite por id o slug, o null si no existe.
    /// </summary>
    Task<TramiteDetail?> GetAsync(string tramiteId, CancellationToken cancellationToken = default);
}
