namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que la ficha de un producto promete (envío, devoluciones, garantía): sólo lo que la tienda DECLARA en
/// <c>Synergos:Tienda:Promesas</c>, con el texto que el editor escribió en el diccionario (016-contenido-piezas).
/// </summary>
/// <remarks>
/// La ficha pintaba siempre las tres promesas del diccionario. Ahora se pinta una promesa si la tienda la declara y
/// su texto no está vacío; el orden es el de siempre (envío, devoluciones, garantía), no el de la configuración.
/// </remarks>
public static class PromesasDeLaFicha
{
    /// <summary>Las promesas que la ficha sabe decir: el nombre con que se declaran y su clave del diccionario.</summary>
    public static readonly IReadOnlyList<(string Nombre, string Clave)> Conocidas =
    [
        ("Envio", "Shop.Product.Trust.Shipping"),
        ("Devoluciones", "Shop.Product.Trust.Returns"),
        ("Garantia", "Shop.Product.Trust.Warranty"),
    ];

    /// <summary>Los textos a pintar, en orden. Vacío si la tienda no declara nada.</summary>
    public static IReadOnlyList<string> Textos(IEnumerable<string>? declaradas, Func<string, string?> diccionario)
    {
        ArgumentNullException.ThrowIfNull(diccionario);
        var si = new HashSet<string>(
            (declaradas ?? []).Where(d => !string.IsNullOrWhiteSpace(d)).Select(d => d.Trim()),
            StringComparer.OrdinalIgnoreCase);
        return Conocidas
            .Where(p => si.Contains(p.Nombre))
            .Select(p => diccionario(p.Clave))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t!)
            .ToList();
    }
}
