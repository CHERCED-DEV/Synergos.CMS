using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services.Diccionario;

/// <summary>
/// Las claves que el bridge publica en <c>window.synergos.i18n.keys</c> para unas secciones y una
/// cultura, con <b>fallback por clave</b> a la cultura por defecto (ADR 0136 §3).
/// </summary>
/// <remarks>
/// <para><b>El fallback se resuelve acá, al construir el bridge, no en el cliente.</b> Una clave
/// sin traducción en la cultura activa se publica con la de la cultura por defecto de Umbraco
/// (la marcada como default en Idiomas: hoy es-CO). Antes se filtraba por la cultura activa y la
/// clave desaparecía del bridge: <c>t('Common.Buttons.Search')</c> sin respaldo devolvía la clave
/// CRUDA — medido en vivo con la traducción en-US borrada de una copia de la base: 175 claves, la
/// que faltaba fuera. El contrato prometía el fallback (<c>defaultCulture</c>, «for fallback when
/// current key missing») y nadie lo hacía.</para>
///
/// <para><b>Una sección casa por prefijo de alias</b>, sin mayúsculas, como resuelve Umbraco (la
/// columna <c>cmsDictionary.key</c> es <c>COLLATE NOCASE</c>, medido): <c>Slider</c> casa
/// <c>Slider.Next</c>; <c>Common.States</c> casa <c>Common.States.NoResults</c> y no
/// <c>Common.Buttons.Save</c>. Los contenedores (la raíz de cada sección) no tienen texto y no
/// se publican.</para>
/// </remarks>
public sealed class DiccionarioDelBridge
{
    private readonly ILocalizationService _localizacion;

    public DiccionarioDelBridge(ILocalizationService localizacion) => _localizacion = localizacion;

    /// <summary>La cultura por defecto del sitio (la de Umbraco), que es la del fallback.</summary>
    public string CulturaPorDefecto() => _localizacion.GetDefaultLanguageIsoCode();

    /// <summary>
    /// Las claves de <paramref name="secciones"/> en <paramref name="cultura"/>; las que no tienen
    /// texto en ella, con el de <paramref name="culturaPorDefecto"/>; las que no tienen en ninguna
    /// de las dos, fuera.
    /// </summary>
    public IReadOnlyDictionary<string, string> Claves(
        IReadOnlyCollection<string> secciones,
        string cultura,
        string culturaPorDefecto)
    {
        ArgumentNullException.ThrowIfNull(secciones);
        var claves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (secciones.Count == 0)
        {
            return claves;
        }

        foreach (var raiz in _localizacion.GetRootDictionaryItems())
        {
            Publicar(raiz, secciones, cultura, culturaPorDefecto, claves);
            foreach (var item in _localizacion.GetDictionaryItemDescendants(raiz.Key))
            {
                Publicar(item, secciones, cultura, culturaPorDefecto, claves);
            }
        }

        return claves;
    }

    /// <summary>¿<paramref name="alias"/> pertenece a alguna de <paramref name="secciones"/>?</summary>
    public static bool EnAlgunaSeccion(string alias, IEnumerable<string> secciones)
        => secciones.Any(s => alias.Equals(s, StringComparison.OrdinalIgnoreCase)
                           || alias.StartsWith(s + ".", StringComparison.OrdinalIgnoreCase));

    private static void Publicar(
        IDictionaryItem item,
        IReadOnlyCollection<string> secciones,
        string cultura,
        string culturaPorDefecto,
        Dictionary<string, string> claves)
    {
        if (string.IsNullOrEmpty(item.ItemKey) || !EnAlgunaSeccion(item.ItemKey, secciones))
        {
            return;
        }

        var texto = Texto(item, cultura) ?? Texto(item, culturaPorDefecto);
        if (texto is not null)
        {
            claves[item.ItemKey] = texto;
        }
    }

    private static string? Texto(IDictionaryItem item, string cultura)
    {
        var valor = item.Translations
            .FirstOrDefault(t => string.Equals(t.LanguageIsoCode, cultura, StringComparison.OrdinalIgnoreCase))
            ?.Value;
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }
}
