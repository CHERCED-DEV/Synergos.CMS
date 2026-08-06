namespace Synergos.CMS.Interfaces;

/// <summary>
/// Query seam para búsqueda full-text del catálogo de contenido
/// publicado del CMS. Aísla a controllers/renderers de la decisión
/// concreta de motor (Examine default, swap futuro a Elastic, Algolia,
/// Synergos.API search backend).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.ExamineSearchProvider</c> usando el
/// índice <c>ExternalIndex</c> que Umbraco mantiene auto-actualizado
/// vía notification handlers internos al publish/unpublish/delete.
///
/// Sin caché — Examine cachea internamente. Sin async — el API de
/// Examine es síncrono in-process.
/// </remarks>
public interface ISearchQuery
{
    /// <summary>
    /// Ejecuta la búsqueda y devuelve los hits ordenados por
    /// relevancia (provider-specific). Puede devolver lista vacía si
    /// <see cref="SearchRequest.Query"/> es vacío o no hay matches.
    /// </summary>
    SearchResponse Search(SearchRequest request);
}

/// <summary>
/// Parámetros de una búsqueda. <see cref="Query"/> es obligatorio
/// (vacío produce respuesta vacía). El resto opcional.
/// </summary>
/// <param name="Query">Texto a buscar. Tokenized por el provider.</param>
/// <param name="MaxItems">Máximo de hits a devolver (paginación).
///   Default 20.</param>
/// <param name="Skip">Cuántos hits saltar (paginación). Default 0.</param>
/// <param name="DocTypeAliasFilter">Filtra por alias de DocType — null
///   no filtra. Útil para "buscar solo en blog" o "solo productos".</param>
public sealed record SearchRequest(
    string Query,
    int MaxItems = 20,
    int Skip = 0,
    string? DocTypeAliasFilter = null);

/// <summary>
/// Snapshot inmutable de una respuesta de búsqueda.
/// </summary>
/// <param name="Query">El query original (eco para que la UI lo pinte).</param>
/// <param name="Hits">Lista de hits ordenados por relevancia.</param>
/// <param name="TotalEstimated">Total estimado de hits matching antes
///   de aplicar Skip/MaxItems. Útil para paginación.</param>
/// <param name="ElapsedMilliseconds">Tiempo que tomó la query (telemetría).</param>
public sealed record SearchResponse(
    string Query,
    IReadOnlyList<SearchHit> Hits,
    int TotalEstimated,
    long ElapsedMilliseconds);

/// <summary>
/// Un hit individual ya proyectado a campos editor-facing.
/// </summary>
/// <param name="Url">URL relativa del nodo encontrado.</param>
/// <param name="Title">Título del nodo (Name o seoTitle).</param>
/// <param name="Excerpt">Snippet del contenido — provider-specific
///   (puede ser highlight con &lt;mark&gt; o solo texto plano).</param>
/// <param name="DocTypeAlias">Alias del DocType — útil para agrupar
///   facets ("3 páginas, 2 posts, 1 producto").</param>
/// <param name="DocTypeName">Nombre human-readable del DocType.</param>
/// <param name="Score">Score de relevancia provider-specific. Mayor
///   = más relevante. Útil solo para debugging.</param>
public sealed record SearchHit(
    string Url,
    string Title,
    string? Excerpt,
    string DocTypeAlias,
    string DocTypeName,
    float Score);
