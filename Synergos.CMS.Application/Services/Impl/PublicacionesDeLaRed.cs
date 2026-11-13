using System.Text.RegularExpressions;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Lo que se lee del cuerpo de una publicación de la red: sus etiquetas, sus menciones y el texto alternativo de su
/// imagen cuando nadie lo escribió (012-blogs-social, I4).
/// </summary>
/// <remarks>
/// Era privado de <c>BlogsController</c>. Lo leen ahora la interina y el catálogo de la red
/// (<see cref="CatalogoDeBlogs"/>), y las dos tienen que sacar las mismas etiquetas del mismo cuerpo: una faceta que
/// no casara con lo que la tarjeta muestra prometería publicaciones que el filtro no devuelve.
/// </remarks>
public static class PublicacionesDeLaRed
{
    /// <summary>Lo más largo que lleva un resumen del cuerpo (el texto alternativo derivado, el título).</summary>
    public const int LargoDelResumen = 80;

    /// <summary>Los hashtags (<c>#tag</c>) del cuerpo, sin el <c>#</c> y en minúsculas, en orden y con repetidos.</summary>
    public static IReadOnlyList<string> Etiquetas(string? cuerpo) => Marcas(cuerpo, @"#(\w[\w-]*)");

    /// <summary>Las menciones (<c>@alias</c>) del cuerpo, sin la <c>@</c> y en minúsculas, en orden y con repetidas.</summary>
    public static IReadOnlyList<string> Menciones(string? cuerpo) => Marcas(cuerpo, @"@(\w[\w-]*)");

    /// <summary>
    /// El texto alternativo de una imagen que nadie describió: el comienzo del cuerpo, o «Imagen del post» si no hay
    /// cuerpo. Es un apaño para pintar, no un dato de quien publicó (ver <c>ContentStreamItem.MediaAlt</c>).
    /// </summary>
    public static string TextoAlternativo(string? cuerpo)
    {
        var resumen = Resumen(cuerpo);
        return resumen.Length == 0 ? "Imagen del post" : resumen;
    }

    /// <summary>
    /// El comienzo del cuerpo, recortado a <see cref="LargoDelResumen"/> caracteres con «…» si sigue; vacío si no hay
    /// cuerpo.
    /// </summary>
    public static string Resumen(string? cuerpo)
    {
        var texto = (cuerpo ?? string.Empty).Trim();
        return texto.Length <= LargoDelResumen ? texto : texto[..LargoDelResumen].TrimEnd() + "…";
    }

    private static IReadOnlyList<string> Marcas(string? cuerpo, string patron)
    {
        if (string.IsNullOrWhiteSpace(cuerpo))
        {
            return Array.Empty<string>();
        }

        var marcas = new List<string>();
        foreach (Match marca in Regex.Matches(cuerpo, patron))
        {
            marcas.Add(marca.Groups[1].Value.ToLowerInvariant());
        }

        return marcas;
    }
}
