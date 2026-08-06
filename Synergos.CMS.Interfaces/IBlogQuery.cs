namespace Synergos.CMS.Interfaces;

/// <summary>
/// Query seam para listar posts editoriales (DocType <c>postPage</c>).
/// Aísla a los renderers Razor (ArticleList, BlogHighlight,
/// PostCategoryPage) de la lógica de búsqueda en el árbol Umbraco —
/// el service consume <c>IUmbracoContextAccessor</c> internamente y
/// proyecta los nodos a <see cref="PostSummary"/> records.
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultBlogQuery</c> porque depende de
/// <c>IUmbracoContextAccessor</c>. Sin paginación stateful — el caller
/// pasa <c>Skip</c>+<c>MaxItems</c> en cada query.
///
/// Sin caché por ahora: Umbraco mantiene el published cache en memoria
/// y la query es O(N posts) con filtros simples. Si llega un sitio con
/// 10k+ posts, se evalúa caching o índice externo (Examine).
/// </remarks>
public interface IBlogQuery
{
    /// <summary>
    /// Devuelve los posts que cumplen el filtro, ordenados por
    /// <c>publishDate</c> descendente (más reciente primero). Posts
    /// sin <c>publishDate</c> se ordenan por nombre del nodo.
    /// </summary>
    IReadOnlyList<PostSummary> GetPosts(BlogQueryRequest request);

    /// <summary>
    /// Devuelve posts relacionados con el post de clave <paramref name="postKey"/>:
    /// los que comparten tags (peso mayor) o la misma categoría, excluyendo el
    /// propio post, ordenados por relevancia y fecha. Vacío si no hay coincidencias.
    /// </summary>
    IReadOnlyList<PostSummary> GetRelated(Guid postKey, int maxItems);

    /// <summary>
    /// Devuelve los posts del sitio actual que llevan el tag
    /// <paramref name="tag"/> (match case-insensitive, exacto sobre cada
    /// tag), ordenados por <c>publishDate</c> descendente. Usado por la
    /// tag page (Ola 230, ADR 0100). Vacío si <paramref name="tag"/> está
    /// en blanco o ningún post lo lleva. Atajo sobre
    /// <see cref="GetPosts"/> con <c>TagsCsv</c> de un solo tag.
    /// </summary>
    IReadOnlyList<PostSummary> GetByTag(string tag, int maxItems);
}

/// <summary>
/// Filtros para la query de posts. Todos opcionales. Cuando todos
/// vacíos, devuelve los <c>MaxItems</c> posts más recientes del sitio.
/// </summary>
/// <param name="MaxItems">Máximo de posts a devolver. Default 6.</param>
/// <param name="Skip">Cuántos saltar (paginación). Default 0.</param>
/// <param name="CategoryAliasOrName">Nombre o alias de
///   <c>postCategoryPage</c> a filtrar. Vacío = todas las categorías.</param>
/// <param name="TagsCsv">Tags separados por coma. Match OR — un post
///   incluye si tiene cualquiera de los tags. Vacío = sin filtro.</param>
/// <param name="AuthorKey">Clave (Key) del autor a filtrar — match contra
///   <c>authorRef</c> del post. Null = sin filtro de autor.</param>
public sealed record BlogQueryRequest(
    int MaxItems = 6,
    int Skip = 0,
    string? CategoryAliasOrName = null,
    string? TagsCsv = null,
    Guid? AuthorKey = null);

/// <summary>
/// Snapshot inmutable de un post para listings. Producido por
/// <see cref="IBlogQuery"/>. Las plantillas Razor lo consumen sin
/// tocar Umbraco directamente.
/// </summary>
/// <param name="Url">URL relativa del post.</param>
/// <param name="Title">Nombre del post (usar como heading H2/H3).</param>
/// <param name="Excerpt">Resumen corto opcional para mostrar bajo el título.</param>
/// <param name="HeroImageUrl">URL absoluta o relativa de la imagen
///   destacada del post. Null si el post no tiene <c>heroImage</c>.</param>
/// <param name="PublishDate">Fecha de publicación. Null si el post no
///   tiene <c>publishDate</c> (se usa el sort fallback al nombre).</param>
/// <param name="ReadTimeMinutes">Minutos estimados de lectura. Null si
///   el post no tiene <c>readTimeMinutes</c>.</param>
/// <param name="CategoryName">Nombre de la categoría padre del post,
///   o null si el post no está bajo una <c>postCategoryPage</c>.</param>
/// <param name="Tags">Tags del post (de <c>compTagging.tags</c>).
///   Colección vacía si no tiene.</param>
public sealed record PostSummary(
    string Url,
    string Title,
    string? Excerpt,
    string? HeroImageUrl,
    DateTime? PublishDate,
    int? ReadTimeMinutes,
    string? CategoryName,
    IReadOnlyCollection<string> Tags);
