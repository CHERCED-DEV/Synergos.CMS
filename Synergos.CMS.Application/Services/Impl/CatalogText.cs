using System.Globalization;
using System.Text;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Comparación de texto para búsqueda de catálogo, insensible a mayúsculas <b>y a
/// diacríticos</b> (T5, doc 25).
/// </summary>
/// <remarks>
/// <b>El bug que cierra (estaba VIVO en los 5 verticales):</b> los catálogos filtraban con
/// <c>.Contains(text, StringComparison.OrdinalIgnoreCase)</c>, que pliega mayúsculas pero
/// <b>NO tildes</b>, sobre un seed es-CO cargado de ellas. Verificado en vivo antes de
/// escribir esto: <c>"Tecnología"</c> → 2 resultados pero <c>"tecnologia"</c> → <b>0</b>;
/// igual con <c>bogota</c>, <c>medellin</c>, <c>registraduria</c>. Un usuario escribiendo
/// desde el móvil —donde nadie pone tildes— no encontraba NADA.
///
/// <para>Vive en UN solo lugar para los 5 catálogos: la regla de oro del doc 25 (ninguna
/// capacidad transversal se implementa dos veces) — la misma razón por la que el proyecto
/// colapsó sus 4 stores en <see cref="Synergos.CMS.Interfaces.IJsonEntityStore"/>.</para>
///
/// <para><b>Por qué a mano y no con la cultura:</b> <c>CompareOptions.IgnoreNonSpace</c>
/// exige ICU y este entorno ya demostró que su globalización es frágil (los 9 tests rojos
/// pre-existentes de formato es-CO). El plegado por <c>FormD</c> es determinista y no
/// depende del entorno.</para>
///
/// <para><b>Por qué público</b> (a diferencia de <see cref="BestEffort"/>): la búsqueda de
/// Blogs vive en un controller de Web, y Web no ve los <c>internal</c> de Application. O es
/// público o la regla se implementa dos veces. No va en <c>Interfaces</c>: eso es para
/// seams con implementación intercambiable, y esto es una regla de comparación fija.</para>
/// </remarks>
public static class CatalogText
{
    /// <summary>
    /// Sustituto privado (Unicode Private Use Area) que protege la <c>ñ</c> del plegado.
    /// PUA: ningún dato real lo contiene, así que no puede colisionar con el texto.
    /// </summary>
    private const char EnyeSentinel = (char)0xE000;

    /// <summary>
    /// Pliega para comparar: quita las tildes y baja a minúsculas invariantes.
    /// <c>"Bogotá"</c> → <c>"bogota"</c>, <c>"Tecnología"</c> → <c>"tecnologia"</c>.
    /// <b>La <c>ñ</c> se preserva</b> (<c>"Diseño"</c> → <c>"diseño"</c>).
    /// </summary>
    /// <remarks>
    /// <b>Por qué la ñ NO se pliega, aunque Unicode la trate como <c>n</c> + tilde
    /// combinante:</b> es una letra propia del español, no un acento. Se pliega lo que el
    /// usuario NO escribe y se respeta lo que SÍ escribe: en un teclado español la <c>ñ</c>
    /// tiene tecla propia y la gente la escribe, mientras la tilde exige long-press en móvil
    /// y casi nadie la pone. Plegarla además colisionaría <c>"año"</c> con <c>"ano"</c>, que
    /// en un catálogo es-CO no es aceptable.
    /// </remarks>
    public static string Fold(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // FormC primero: el dato puede traer la ñ YA descompuesta (n + U+0303) según su
        // fuente, y entonces el Replace de abajo no la vería y el filtro se comería su
        // tilde, degradando "año" a "ano". Componer la vuelve un solo char reconocible.
        var composed = value.Normalize(NormalizationForm.FormC);

        // Se aparta la ñ ANTES de descomponer: si no, FormD la parte en n + U+0303 y el
        // filtro de abajo se come la tilde igual.
        var protectedValue = composed.Replace('ñ', EnyeSentinel).Replace('Ñ', EnyeSentinel);

        var decomposed = protectedValue.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            // NonSpacingMark = la tilde/diéresis ya separada de su letra por FormD.
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb.ToString()
            .Normalize(NormalizationForm.FormC)
            .Replace(EnyeSentinel, 'ñ')   // el resultado se compara plegado-contra-plegado
            .ToLowerInvariant();
    }

    /// <summary>
    /// ¿<paramref name="haystack"/> contiene <paramref name="needle"/> ignorando tildes y
    /// mayúsculas? Reemplaza al <c>.Contains(t, OrdinalIgnoreCase)</c> de los catálogos.
    /// Un needle vacío no filtra (devuelve true), igual que el comportamiento previo.
    /// </summary>
    public static bool Contains(string? haystack, string? needle)
    {
        if (string.IsNullOrWhiteSpace(needle))
        {
            return true;
        }
        return Fold(haystack).Contains(Fold(needle), StringComparison.Ordinal);
    }

    /// <summary>
    /// Igualdad exacta plegada — para facetas/categorías (<c>"Tecnología" == "tecnologia"</c>).
    /// </summary>
    /// <remarks>
    /// No se llama <c>Equals</c> a propósito: sombrearía <see cref="object.Equals(object?, object?)"/>
    /// y un futuro <c>CatalogText.Equals(x, y)</c> con dos no-strings compilaría comparando
    /// referencias en silencio.
    /// </remarks>
    public static bool AreEqual(string? a, string? b)
        => string.Equals(Fold(a), Fold(b), StringComparison.Ordinal);

    /// <summary>
    /// Parte una consulta en tokens plegados: <c>"Laptop  Pro-14"</c> →
    /// <c>["laptop", "pro", "14"]</c>. Vacío si no queda nada buscable.
    /// </summary>
    /// <remarks>
    /// Se corta por todo lo que no sea letra o dígito, así que la puntuación y los guiones
    /// no parten la búsqueda: quien escribe "pro-14" y quien escribe "pro 14" busca lo mismo.
    /// </remarks>
    public static IReadOnlyList<string> Tokenize(string? value)
    {
        var folded = Fold(value);
        if (folded.Length == 0)
        {
            return Array.Empty<string>();
        }

        var tokens = new List<string>();
        var start = -1;
        for (var i = 0; i <= folded.Length; i++)
        {
            var isWordChar = i < folded.Length && char.IsLetterOrDigit(folded[i]);
            if (isWordChar && start < 0)
            {
                start = i;
            }
            else if (!isWordChar && start >= 0)
            {
                tokens.Add(folded[start..i]);
                start = -1;
            }
        }
        return tokens;
    }

    /// <summary>
    /// Cuánto casa <paramref name="token"/> (ya plegado) dentro de <paramref name="text"/>:
    /// <b>3</b> = alguna palabra es exactamente el token, <b>1</b> = alguna palabra empieza
    /// por él, <b>0</b> = no casa.
    /// </summary>
    /// <remarks>
    /// El prefijo es lo que hace que "zapa" encuentre "zapatos" — ni un <c>.Contains</c> ni
    /// el <c>GroupedOr</c> de Examine lo dan. Se compara por PALABRA y no por substring
    /// suelto para que "ola" no encuentre "chocolate".
    /// </remarks>
    public static int ScoreToken(string? text, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return 0;
        }

        var best = 0;
        foreach (var word in Tokenize(text))
        {
            if (string.Equals(word, token, StringComparison.Ordinal))
            {
                return 3;   // exacto: no hay nada mejor, se corta aquí
            }
            if (word.StartsWith(token, StringComparison.Ordinal))
            {
                best = 1;
            }
        }
        return best;
    }
}
