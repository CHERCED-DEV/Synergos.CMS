namespace Synergos.CMS.Application.Output.Config;

/// <summary>
/// Objeto de configuración completo de una página, listo para serializar como JSON.
///
/// Este DTO es el contrato entre el CMS (Umbraco/MVC) y el frontend (Angular).
/// Contiene todo lo necesario para que Angular renderice la página sin acceder
/// directamente a la API de Umbraco.
///
/// Flujo:
///   IPublishedContent
///     → PageConfigAssembler.Build()
///       → PageConfig (este objeto)
///         → GET /api/synergos/v1/page → JSON
///           → Angular / Web Components
/// </summary>
public sealed record PageConfig(
    string                    Title,
    string?                   Subtitle,
    SeoConfig                 Seo,
    PageDomConfig             Dom,
    IReadOnlyList<BlockConfig> Sections
);
