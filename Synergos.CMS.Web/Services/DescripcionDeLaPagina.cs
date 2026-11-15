namespace Synergos.CMS.Web.Services;

/// <summary>
/// La descripción que una página da al compartirse y a los buscadores (<c>meta description</c>, <c>og:description</c>
/// y <c>twitter:description</c>), en <c>Views/Shared/_SeoHead.cshtml</c>.
/// </summary>
/// <remarks>
/// <para><b>Por qué (016, 4.ª vuelta, C9).</b> 75 de 112 páginas del piloto salían sin descripción: sólo se leía la
/// <c>seoDescription</c> de la página y la de la marca. Una ficha de producto, de inmueble o de equipo ya tiene su
/// resumen escrito para el visitante, y una página de un sitio que no tiene el suyo dice, por lo menos, lo que dice
/// su sitio.</para>
/// <para>El orden: la de la página → su resumen propio (<see cref="ResumenesPropios"/>) → la de la marca → la de su
/// sitio. El texto sale plano y sin cortar palabras, a lo más <see cref="Largo"/> caracteres.</para>
/// </remarks>
public static class DescripcionDeLaPagina
{
    /// <summary>Los resúmenes que las fichas ya traen, en el orden en que se prefieren.</summary>
    public static readonly IReadOnlyList<string> ResumenesPropios =
        ["productDescriptionText", "categoryDescription", "equipmentSummary"];

    /// <summary>El largo máximo; los buscadores muestran unos 155–160 caracteres.</summary>
    public const int Largo = 160;

    /// <param name="dePagina">El valor de una propiedad de la página (nulo si no la tiene).</param>
    /// <param name="deLaMarca">La descripción por defecto de la marca (<c>siteConfigSettings</c>).</param>
    /// <param name="deSuSitio">La <c>seoDescription</c> del siteRoot de la página.</param>
    public static string? Resolver(Func<string, string?> dePagina, string? deLaMarca, string? deSuSitio)
    {
        var candidatas = new List<string?> { dePagina("seoDescription") };
        candidatas.AddRange(ResumenesPropios.Select(dePagina));
        candidatas.Add(deLaMarca);
        candidatas.Add(deSuSitio);

        foreach (var candidata in candidatas)
        {
            var texto = Plano(candidata);
            if (texto.Length > 0) { return Recortar(texto); }
        }
        return null;
    }

    /// <summary>Sin etiquetas HTML ni espacios de más.</summary>
    internal static string Plano(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) { return string.Empty; }
        var sinEtiquetas = System.Text.RegularExpressions.Regex.Replace(texto, "<[^>]+>", " ");
        var decodificado = System.Net.WebUtility.HtmlDecode(sinEtiquetas);
        return System.Text.RegularExpressions.Regex.Replace(decodificado, @"\s+", " ").Trim();
    }

    /// <summary>A lo más <see cref="Largo"/> caracteres, cortando en el último espacio y con «…».</summary>
    internal static string Recortar(string texto)
    {
        if (texto.Length <= Largo) { return texto; }
        var corte = texto.LastIndexOf(' ', Largo - 1);
        var cabeza = (corte > Largo / 2 ? texto[..corte] : texto[..(Largo - 1)]).TrimEnd(' ', ',', ';', ':', '.', '—', '·');
        return cabeza + "…";
    }
}
