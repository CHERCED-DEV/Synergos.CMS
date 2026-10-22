namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// Si el árbol publicado ya muestra la raíz del sitio de alquiler: el <c>siteRoot</c> cuyo <c>brandKey</c> es el de
/// <c>Synergos:Catalog:Scopes:Alquiler</c>, con la MISMA regla con que lo busca <see cref="UmbracoEquipmentCatalogSource"/>.
/// </summary>
/// <remarks>
/// La pregunta es de quien decide algo por lo que el catálogo NO sirve: sin la raíz el catálogo sale vacío para todos
/// los equipos, y eso no dice nada de ninguno (línea 005).
/// </remarks>
public interface IRaizDelAlquiler
{
    /// <summary>Si la raíz se ve; sin escribir nada en el registro.</summary>
    /// <returns><see langword="true"/> si el árbol publicado la muestra.</returns>
    bool SeVe();
}
