using System.Globalization;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Gravedad de lo que se encontró al leer un <c>postPage</c>.</summary>
public enum SocialContentIssueLevel
{
    /// <summary>Se sirvió igual, con un valor derivado o ausente.</summary>
    Warning,

    /// <summary>No se pudo servir ese dato, o el post entero se omite.</summary>
    Error,
}

/// <summary>Algo que el editor tiene que arreglar, con el texto que lo dice.</summary>
public sealed record SocialContentIssue(SocialContentIssueLevel Level, string Message);

/// <summary>Lo construido más lo que hubo que señalar por el camino.</summary>
public sealed record SocialContentResult<T>(T Value, IReadOnlyList<SocialContentIssue> Issues);

/// <summary>
/// Las reglas de leer un <c>postPage</c>: qué se acepta, qué se deriva y qué hace que un post no
/// se pueda sembrar. Lógica PURA — no conoce Umbraco, así que se prueba sin levantar un contexto.
/// </summary>
/// <remarks>
/// Calca <see cref="ProfessionalContentRules"/> y <see cref="CourseContentRules"/>: la fuente
/// recorre el árbol y ésta decide.
/// </remarks>
public static class SocialContentRules
{
    /// <summary>
    /// Qué es un post editorial para el feed.
    /// </summary>
    /// <remarks>
    /// <c>article</c> y no <c>post</c>: el vocabulario de <c>ContentStreamItem.Kind</c> ya
    /// distingue el apunte corto que alguien escribe desde la app del texto largo que se autora
    /// y se publica con su URL, y la tarjeta los pinta distinto. Sembrarlos como <c>post</c>
    /// haría pasar por apunte lo que es un artículo — no rompe nada y cuenta otra cosa.
    /// </remarks>
    public const string ArticleKind = "article";

    /// <summary>
    /// La fecha que el editor escribió en <c>publishDate</c>, o «no consta».
    /// </summary>
    /// <remarks>
    /// <para><b><c>publishDate</c> es un <c>Umbraco.TextBox</c></b> —lo dice el DocType, y no se
    /// cambia acá— así que lo que llega es lo que alguien tecleó. Se lee en
    /// <see cref="CultureInfo.InvariantCulture"/> con formatos explícitos y **nunca se adivina**:
    /// leer «03/04/2026» con la cultura del servidor da marzo o abril según dónde corra, y una
    /// fecha plausible equivocada por un mes no la ve nadie — es
    /// <c>feedback_a_failed_tryparse_is_not_a_value</c> con un mes en vez de con un precio.</para>
    ///
    /// <para><b>Sin fecha el post se siembra igual.</b> El feed ordena por
    /// <c>CreatedUtc</c>, que lo asigna el stream al sembrar; la del editor es para MOSTRAR. Un
    /// post sin fecha sale con la de su siembra, que es cierta, en vez de no salir.</para>
    /// </remarks>
    public static SocialContentResult<DateTime?> ParsePublishDate(string slug, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SocialContentResult<DateTime?>(null, Array.Empty<SocialContentIssue>());
        }

        string[] formatos = ["yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ssZ"];
        if (DateTime.TryParseExact(
                raw.Trim(), formatos, CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return new SocialContentResult<DateTime?>(parsed, Array.Empty<SocialContentIssue>());
        }

        return new SocialContentResult<DateTime?>(
            null,
            [Warn($"El post '{slug}' tiene publishDate='{raw}', que no es una fecha en formato "
                  + "yyyy-MM-dd. Se sirve sin fecha en vez de adivinar el mes.")]);
    }

    /// <summary>
    /// El handle de un autor a partir del segmento de URL de su <c>authorPage</c>.
    /// </summary>
    /// <remarks>
    /// <b>Derivar esto NO es fabricar</b>, y la distinción es la que importa en esta clase: un
    /// handle es un identificador legible y el slug del nodo ES ese identificador; no afirma nada
    /// sobre la persona. Lo que sí sería fabricar es inventar el NOMBRE, y por eso un
    /// <c>authorPage</c> sin <c>authorName</c> deja el post sin sembrar
    /// (<see cref="CheckAuthor"/>).
    /// </remarks>
    public static string HandleFromSegment(string segment)
        => string.IsNullOrWhiteSpace(segment) ? string.Empty : segment.Trim().Trim('/').ToLowerInvariant();

    /// <summary>
    /// Si un autor se puede firmar, y si no, por qué.
    /// </summary>
    /// <remarks>
    /// <b>Sin nombre NO se siembra</b>, y es deliberado: <c>SocialDemoSeed.AuthorById</c>
    /// resuelve un id desconocido devolviendo el id como handle Y como nombre, así que un post
    /// sembrado sin autor legible sale en el feed firmado por algo que nadie escribió. El
    /// <c>Name</c> del nodo tampoco sirve de respaldo —es del árbol del backoffice, y sale
    /// «Author Page (1)»—, que es la misma decisión que <c>UmbracoProfessionalDirectorySource</c>
    /// tomó con el nombre del médico (#118).
    /// </remarks>
    public static IReadOnlyList<SocialContentIssue> CheckAuthor(string slug, string? handle, string? displayName)
    {
        var issues = new List<SocialContentIssue>();

        if (string.IsNullOrWhiteSpace(handle))
        {
            issues.Add(Error($"El post '{slug}' apunta a un authorPage sin segmento de URL: no hay "
                             + "de dónde sacar un handle. No se siembra."));
        }

        if (string.IsNullOrWhiteSpace(displayName))
        {
            issues.Add(Error($"El post '{slug}' apunta a un authorPage sin authorName. Sembrarlo "
                             + "dejaría la tarjeta firmada por el identificador del nodo, que "
                             + "nadie escribió. No se siembra."));
        }

        return issues;
    }

    /// <summary>
    /// Dos <c>postPage</c> con el mismo slug.
    /// </summary>
    /// <remarks>
    /// El slug es la llave del mapping durable de la siembra, así que dos nodos con el mismo
    /// serían el MISMO post para el feed: el segundo pisaría la huella del primero y los dos se
    /// re-sembrarían en cada vuelta, creciendo el feed sin que nada fallara.
    /// </remarks>
    public static IReadOnlyList<SocialContentIssue> FindSlugCollisions(IEnumerable<string> slugs)
        => slugs
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => Error(
                $"El slug '{g.Key}' está en {g.Count()} postPage. Para el feed son el MISMO post, "
                + "así que cada vuelta los vuelve a sembrar. Renómbralos."))
            .ToList();

    private static SocialContentIssue Warn(string message)
        => new(SocialContentIssueLevel.Warning, message);

    private static SocialContentIssue Error(string message)
        => new(SocialContentIssueLevel.Error, message);
}
