using System.Globalization;
using System.Text;
using Synergos.Core;

namespace Synergos.Api.Catalog.Domain;

/// <summary>Lo que el catálogo rechaza <b>solo</b>, y cómo puntúa.</summary>
public static class CatalogRules
{
    public const string CodePrefix = "catalog";

    public const int MaxTitleLength = 300;
    public const int MaxTags = 32;
    public const int MaxFacets = 32;

    /// <summary>Si el ítem que llega se puede indexar.</summary>
    public static Rejection? CheckIndexable(string? title, IReadOnlyList<string>? tags,
        IReadOnlyDictionary<string, string>? facets)
    {
        if (string.IsNullOrWhiteSpace(title) || title!.Length > MaxTitleLength)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_title",
                $"El título es obligatorio y no pasa de {MaxTitleLength} caracteres.");
        }
        if (tags is not null && tags.Count > MaxTags)
        {
            return Rejection.Invalid($"{CodePrefix}.too_many_tags", $"Hasta {MaxTags} etiquetas.");
        }
        if (facets is not null && facets.Count > MaxFacets)
        {
            return Rejection.Invalid($"{CodePrefix}.too_many_facets", $"Hasta {MaxFacets} facetas.");
        }
        return null;
    }

    /// <summary>Si la búsqueda trae algo con qué buscar.</summary>
    /// <remarks>
    /// Una consulta totalmente vacía sería un volcado del índice. Basta con UNA de las tres
    /// —texto, etiqueta o faceta— porque "todos los inmuebles de Medellín" es una búsqueda
    /// legítima que no lleva texto.
    /// </remarks>
    public static Rejection? CheckQuery(string? text, IReadOnlyList<string>? tags,
        IReadOnlyDictionary<string, string>? facets)
        => !string.IsNullOrWhiteSpace(text) || tags?.Count > 0 || facets?.Count > 0
            ? null
            : Rejection.Invalid($"{CodePrefix}.query_required",
                "Hace falta texto, etiqueta o faceta: sin nada de eso esto es un volcado del índice.");

    /// <summary>
    /// Normaliza para COMPARAR: minúsculas, sin espacios sobrantes y sin tildes — <b>incluida
    /// la ñ</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>Quitar las tildes no es descuido:</b> en es-CO la gente busca "medellin" y
    /// "bogota" sin acento la mitad de las veces, y un índice que distingue devuelve cero
    /// resultados para una consulta perfectamente razonable.</para>
    ///
    /// <para><b>La ñ se pliega también, y esta parte se decidió dos veces.</b> El primer intento
    /// la preservaba, con el argumento de que no es un acento sino una letra distinta y de que
    /// "año" y "ano" no son lo mismo. Suena bien y en la práctica es peor: levantando el
    /// servicio, buscar <c>diseno</c> no encontraba "Curso de diseño" — <b>cero resultados para
    /// una consulta normal</b>, que es el peor desenlace posible de un catálogo. Conflatar
    /// "año/ano" solo agrega un resultado de más; no encontrar nada no tiene arreglo del lado
    /// del usuario, porque no sabe qué está escribiendo mal.</para>
    ///
    /// <para>Y el costo es solo de COMPARACIÓN: lo que se muestra es <c>Title</c> sin tocar, así
    /// que la ñ nunca desaparece de la pantalla. Es lo mismo que hacen los analizadores de
    /// español al indexar.</para>
    /// </remarks>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var descompuesto = raw.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(descompuesto.Length);
        foreach (var c in descompuesto)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// Cuánto puntúa un ítem contra un texto. Cero = no coincide.
    /// </summary>
    /// <remarks>
    /// El título pesa más que el resumen. Sin esa diferencia, un ítem que menciona la palabra de
    /// pasada en su descripción sale por encima del que se llama exactamente así — que es la
    /// forma más rápida de que la búsqueda parezca rota.
    /// </remarks>
    public static int Score(CatalogItem item, string normalizedText)
    {
        if (normalizedText.Length == 0) return 1;   // sin texto, todos los que pasaron los filtros valen igual

        var titulo = Normalize(item.Title);
        var resumen = Normalize(item.Summary);

        var puntos = 0;
        if (titulo.Equals(normalizedText, StringComparison.Ordinal)) puntos += 100;
        else if (titulo.Contains(normalizedText, StringComparison.Ordinal)) puntos += 50;

        if (resumen.Contains(normalizedText, StringComparison.Ordinal)) puntos += 10;

        if (item.Tags.Any(t => Normalize(t).Equals(normalizedText, StringComparison.Ordinal))) puntos += 30;

        return puntos;
    }
}
