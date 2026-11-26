namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>
/// Si el árbol publicado ya muestra la raíz del sitio de Salud: el <c>siteRoot</c> cuyo <c>brandKey</c> es el de
/// <c>Synergos:Catalog:Scopes:Salud</c>, con la MISMA regla con que lo busca <see cref="UmbracoProfessionalDirectorySource"/>.
/// La receta de <see cref="IRaizDelAlquiler"/> (línea 005), para Salud.
/// </summary>
/// <remarks>
/// La pregunta es de quien decide algo por lo que el directorio NO sirve: en un import la raíz todavía no está en la
/// caché, el directorio sale vacío para todos y eso no dice nada de ningún profesional (003-salud-cita, D-003-V2: el
/// ensayo de reconstrucción contaba tres errores al publicar los tres profesionales).
/// </remarks>
public interface IRaizDeSalud
{
    /// <summary>Si la raíz se ve; sin escribir nada en el registro.</summary>
    /// <returns><see langword="true"/> si el árbol publicado la muestra.</returns>
    bool SeVe();
}

/// <summary>Con la muestra de desarrollo (<c>Sources:Salud = demo</c>) no hay raíz que esperar.</summary>
internal sealed class RaizDeSaludDeLaMuestra : IRaizDeSalud
{
    public bool SeVe() => true;
}
