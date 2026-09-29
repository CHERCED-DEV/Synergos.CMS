using System.Globalization;
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
///
/// <para><b>Lo que el editor escribió mal no tumba la página ni se pierde callado.</b> Un número
/// que no es número deja la clave fuera —el elemento aplica su valor por defecto— y se anota en el
/// log con el bloque y la propiedad (ADR 0135 §2: fallar ruidosamente, nunca un widget vacío en
/// silencio).</para>
/// </remarks>
public sealed class LectorDelEditor
{
    private readonly IPublishedElement _elemento;
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger? _log;

    public LectorDelEditor(IPublishedElement elemento, IPublishedValueFallback fallback, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(elemento);
        ArgumentNullException.ThrowIfNull(fallback);
        _elemento = elemento;
        _fallback = fallback;
        _log = log;
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

    /// <summary>
    /// Un número que el editor escribió como texto (<c>"4.5"</c>, <c>"4,5"</c>); <c>null</c> si lo
    /// dejó vacío o no es un número.
    /// </summary>
    /// <remarks>
    /// Se acepta la coma decimal porque es la del editor es-CO; lo que no sea un número se anota y
    /// no viaja. No se acotan rangos: el rango es regla del elemento, que es quien lo pinta.
    /// </remarks>
    public decimal? Numero(string alias)
    {
        var texto = Texto(alias);
        if (texto is null)
        {
            return null;
        }

        if (decimal.TryParse(texto.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var numero))
        {
            return numero;
        }

        NoEsValido(alias, texto, "un número");
        return null;
    }

    /// <summary>Un entero que el editor escribió como texto; <c>null</c> si vacío o no entero.</summary>
    public int? Entero(string alias)
    {
        var texto = Texto(alias);
        if (texto is null)
        {
            return null;
        }

        if (int.TryParse(texto, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entero))
        {
            return entero;
        }

        NoEsValido(alias, texto, "un entero");
        return null;
    }

    /// <summary>Anota lo que el editor escribió y no se pudo usar: no viaja, pero no se pierde callado.</summary>
    public void NoEsValido(string alias, string valor, string esperado)
        => _log?.LogWarning(
            "El bloque {Bloque} ({Tipo}) trae en «{Alias}» algo que no es {Esperado}: «{Valor}». No viaja al elemento.",
            _elemento.Key,
            _elemento.ContentType?.Alias,
            alias,
            esperado,
            valor);
}
