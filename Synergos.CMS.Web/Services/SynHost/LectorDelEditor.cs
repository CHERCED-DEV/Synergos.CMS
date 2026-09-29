using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// Lee lo que el editor autoró en un bloque, con la sobrecarga de Umbraco que recibe el
/// <see cref="IPublishedValueFallback"/> en vez de sacarlo del proveedor estático.
/// </summary>
/// <remarks>
/// <para><b>Por qué no <c>Model.Value&lt;string&gt;("alias")</c></b>, que es lo que hacían las
/// vistas: esa forma «amigable» saca el fallback de <c>StaticServiceProvider</c>, así que un
/// resolver escrito con ella no se puede probar sin arrancar Umbraco. Con el fallback inyectado
/// es la misma lectura —misma cultura, mismo segmento— y un test la alimenta con un
/// <c>IPublishedElement</c> falso.</para>
///
/// <para>Es COMPOSICIÓN, no una base: cada resolver crea uno y le pide lo que necesita.</para>
/// </remarks>
public sealed class LectorDelEditor
{
    private readonly IPublishedElement _elemento;
    private readonly IPublishedValueFallback _fallback;

    public LectorDelEditor(IPublishedElement elemento, IPublishedValueFallback fallback)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        ArgumentNullException.ThrowIfNull(fallback);
        _elemento = elemento;
        _fallback = fallback;
    }

    /// <summary>
    /// El texto de <paramref name="alias"/>, recortado; <c>null</c> si el editor lo dejó vacío.
    /// </summary>
    /// <remarks>
    /// Vacío y ausente son lo mismo para el elemento, y así la clave no viaja: el emitter omite
    /// los nulos y el elemento aplica su valor por defecto.
    /// </remarks>
    public string? Texto(string alias)
    {
        var crudo = _elemento.Value<string>(_fallback, alias);
        return string.IsNullOrWhiteSpace(crudo) ? null : crudo.Trim();
    }
}
