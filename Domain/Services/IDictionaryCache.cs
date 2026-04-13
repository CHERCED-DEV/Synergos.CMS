namespace Synergos.CMS.Domain.Services;

/// <summary>
/// Cache de dictionary items de Umbraco.
///
/// Evita llamar a ILocalizationService en cada render — los dictionary items
/// son datos estáticos que solo cambian cuando un editor los modifica en el backoffice.
///
/// La implementación almacena los valores en IMemoryCache con expiración configurable
/// y los invalida automáticamente cuando se recibe una notificación de cambio de contenido.
/// </summary>
public interface IDictionaryCache
{
    /// <summary>
    /// Devuelve el valor del dictionary item para la clave y cultura indicadas.
    /// Si la clave no existe o no tiene traducción, devuelve <paramref name="fallback"/>.
    /// </summary>
    string Get(string key, string? culture = null, string fallback = "");

    /// <summary>
    /// Devuelve todos los dictionary items como un diccionario plano key → value.
    /// Útil para pasar el mapa completo de traducciones en el config de un web component
    /// (patrón MCPL: <c>config.translations["Key"]</c> en lugar de strings hardcodeados).
    /// El resultado se cachea bajo una sola entrada durante el mismo TTL que <see cref="Get"/>.
    /// </summary>
    IReadOnlyDictionary<string, string> GetAll(string? culture = null);

    /// <summary>
    /// Devuelve un subconjunto de dictionary items filtrado por las claves indicadas.
    /// Equivalente a <see cref="GetAll"/> pero más eficiente cuando el componente
    /// solo necesita un subconjunto conocido de traducciones.
    /// </summary>
    IReadOnlyDictionary<string, string> GetMany(IEnumerable<string> keys, string? culture = null);

    /// <summary>
    /// Invalida todas las entradas en caché. Llamado desde notificaciones de cambio de contenido.
    /// </summary>
    void Invalidate();
}
