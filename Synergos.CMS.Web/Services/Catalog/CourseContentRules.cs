using System.Globalization;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Gravedad de lo que se encontró al leer un <c>coursePage</c>.</summary>
public enum CourseContentIssueLevel
{
    /// <summary>Se sirvió igual, con un valor derivado o recortado.</summary>
    Warning,

    /// <summary>No se pudo servir: el curso se omite del catálogo.</summary>
    Error,
}

/// <summary>Algo que el editor tiene que arreglar, con el texto que lo dice.</summary>
public sealed record CourseContentIssue(CourseContentIssueLevel Level, string Message);

/// <summary>Lo construido más lo que hubo que señalar por el camino.</summary>
public sealed record CourseContentResult<T>(T Value, IReadOnlyList<CourseContentIssue> Issues);

/// <summary>
/// Las reglas de leer un <c>coursePage</c>: qué se acepta, qué se deriva y qué hace que un
/// curso no se pueda servir. Lógica PURA — no conoce Umbraco, así que se prueba sin levantar
/// un contexto.
/// </summary>
/// <remarks>
/// Calca <c>EventContentRules</c>: la fuente recorre el árbol y ésta decide. La separación es
/// lo que hace que las decisiones del editor se puedan probar sin un `IPublishedContent`.
/// </remarks>
public static class CourseContentRules
{
    /// <summary>
    /// Vocabulario interno de nivel. <b>Es el del seed —español— a propósito.</b>
    /// </summary>
    /// <remarks>
    /// <b>El campo admite dos vocabularios y aquí se colapsan a uno.</b> El schema le pide al
    /// editor <c>beginner|intermediate|advanced</c>; el seed de demo lleva
    /// <c>Principiante|Intermedio|Avanzado</c>; y el filtro por nivel del catálogo compara el
    /// valor CRUDO. Sin normalizar, filtrar por «Intermedio» no encontraría los cursos
    /// autorados como <c>intermediate</c> aunque sean lo mismo — y no fallaría: devolvería
    /// menos cursos, que es lo que no se ve.
    ///
    /// <para>Se colapsa al del seed porque es el que ya usa el resto del motor y el que el
    /// controller traduce a la UI. Cualquier otro valor cae a principiante y se avisa: un
    /// nivel desconocido elige ETIQUETA, no precio, así que omitir el curso entero por una
    /// errata sería peor que servirlo con el nivel más bajo.</para>
    /// </remarks>
    public static CourseContentResult<string> NormalizeLevel(string slug, string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();

        var level = value switch
        {
            "avanzado" or "advanced" => "Avanzado",
            "intermedio" or "intermediate" => "Intermedio",
            "principiante" or "beginner" or "" => "Principiante",
            _ => null,
        };

        if (level is not null)
        {
            return new CourseContentResult<string>(level, Array.Empty<CourseContentIssue>());
        }

        return new CourseContentResult<string>("Principiante", new[]
        {
            Warn($"coursePage slug='{slug}' tiene courseLevel='{raw}', que no es un nivel conocido. " +
                 "Se sirve como Principiante. Valores admitidos: beginner/intermediate/advanced o " +
                 "Principiante/Intermedio/Avanzado."),
        });
    }

    /// <summary>
    /// El precio, o <c>false</c> si el texto no es INEQUÍVOCAMENTE uno. Vacío vale 0 (gratis).
    /// </summary>
    /// <remarks>
    /// <b>La misma trampa que ya costó dinero en Tienda y en Eventos, la misma regla.</b>
    /// <c>coursePriceFrom</c> es un <c>Umbraco.TextBox</c>, así que el editor teclea texto
    /// libre — y <c>"180.000"</c> SÍ parsea en InvariantCulture, porque ahí el punto es
    /// separador DECIMAL: da <b>180</b>. No es un fallo de parseo sino un precio plausible
    /// equivocado por 1000×, y ninguna guarda de «&gt; 0» lo ve.
    ///
    /// <para><b>Y aquí se OMITE el curso</b>, no se sirve en 0: un 0 se pinta como
    /// <i>Gratis</i>, y el motor de matrícula resuelve el precio real DESDE EL CATÁLOGO como
    /// defensa anti-tampering — así que un precio mal leído no es sólo una etiqueta
    /// equivocada, es lo que se cobra. Que falte una ficha se ve al instante; un curso pago
    /// matriculado gratis se descubre en la contabilidad.</para>
    /// </remarks>
    public static bool TryParsePrice(string? raw, out decimal price)
    {
        price = 0m;
        var value = raw?.Trim();

        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        return value.All(char.IsAsciiDigit)
            && decimal.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out price);
    }

    /// <summary>
    /// El currículum: módulos y lecciones con ids ESTABLES, en el orden en que se cursan.
    /// </summary>
    /// <remarks>
    /// <b>El orden es la posición en la lista, no un campo</b> — es lo que dice el schema y lo
    /// que dice el seam (<c>CourseDraftModule</c>: «orden = posición en la lista»). Un número
    /// de orden junto a una lista arrastrable son dos fuentes para el mismo hecho.
    ///
    /// <para><b>Por qué los ids importan tanto aquí.</b> El id de una lección es la clave con
    /// la que se recuerda qué item del feed lleva su cuerpo y por dónde va cada alumno
    /// (<c>CourseProgress.CompletedLessonIds</c>). Si cambia, la lección se vuelve a sembrar y
    /// el progreso de quien ya la vio deja de contar — en silencio. Por eso el editor puede
    /// FIJARLO (<c>lessonId</c>) y por eso el derivado no lleva el número de orden sino el
    /// título: reordenar el temario es normal y no puede reescribir la historia de nadie.</para>
    ///
    /// <para>Un módulo sin lecciones se descarta con aviso: no aporta nada al temario y sí una
    /// sección vacía en la ficha.</para>
    /// </remarks>
    public static CourseContentResult<IReadOnlyList<AuthoredModule>> BuildCurriculum(
        string slug,
        IReadOnlyList<AuthoredModuleDraft> drafts)
    {
        var issues = new List<CourseContentIssue>();
        var modules = new List<AuthoredModule>();
        var usedModuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedLessonIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var moduleOrder = 1;

        foreach (var draft in drafts)
        {
            var title = draft.Title?.Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                issues.Add(Warn($"coursePage slug='{slug}': un módulo sin título; se omite."));
                continue;
            }

            var moduleId = ResolveId(draft.Id, $"{slug}-{Slugify(title)}", usedModuleIds);

            var lessons = new List<AuthoredLesson>();
            var lessonOrder = 1;
            foreach (var l in draft.Lessons)
            {
                var lessonTitle = l.Title?.Trim();
                if (string.IsNullOrWhiteSpace(lessonTitle))
                {
                    issues.Add(Warn($"coursePage slug='{slug}', módulo '{title}': una lección sin título; se omite."));
                    continue;
                }

                var lessonId = ResolveId(l.Id, $"{moduleId}-{Slugify(lessonTitle)}", usedLessonIds);

                lessons.Add(new AuthoredLesson(
                    Id: lessonId,
                    Title: lessonTitle,
                    Order: lessonOrder,
                    // Una duración negativa no es «menos tiempo»: es una errata que restaría
                    // del total del curso.
                    DurationMinutes: Math.Max(0, l.DurationMinutes),
                    VideoRef: string.IsNullOrWhiteSpace(l.VideoUrl) ? null : l.VideoUrl.Trim(),
                    // Sin cuerpo se siembra el título: el player pide el item del feed igual, y
                    // una lección que devuelve 404 se ve peor que una con una línea.
                    Body: string.IsNullOrWhiteSpace(l.Body) ? lessonTitle : l.Body.Trim(),
                    Resources: BuildResources(l.Resources),
                    IsPreview: l.IsPreview));
                lessonOrder++;
            }

            if (lessons.Count == 0)
            {
                issues.Add(Warn($"coursePage slug='{slug}': el módulo '{title}' no tiene lecciones servibles; se omite."));
                continue;
            }

            modules.Add(new AuthoredModule(moduleId, title, moduleOrder, lessons));
            moduleOrder++;
        }

        return new CourseContentResult<IReadOnlyList<AuthoredModule>>(modules, issues);
    }

    /// <summary>
    /// El material descargable de una lección, a partir de los enlaces que puso el editor.
    /// </summary>
    /// <remarks>
    /// <b>El tipo se deduce de la extensión y no se le pregunta al editor.</b> Un enlace ya
    /// lleva nombre y destino —que son el título y la URL del recurso—, así que pedirle además
    /// un «tipo» sería un campo que se teclea mal y que la extensión ya dice. Lo que no se
    /// reconoce se sirve como <c>link</c>, que es lo honesto: no es un PDF que falló, es un
    /// enlace.
    /// </remarks>
    public static IReadOnlyList<CourseResource> BuildResources(IReadOnlyList<AuthoredResourceDraft>? drafts)
    {
        if (drafts is null || drafts.Count == 0)
        {
            return Array.Empty<CourseResource>();
        }

        var resources = new List<CourseResource>(drafts.Count);
        foreach (var d in drafts)
        {
            var url = d.Url?.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            var title = string.IsNullOrWhiteSpace(d.Title) ? url : d.Title.Trim();
            resources.Add(new CourseResource(title, url, ResourceKind(url)));
        }

        return resources;
    }

    /// <summary>
    /// Cuántas lecciones y cuántos minutos tiene el curso, contados del currículum.
    /// </summary>
    /// <remarks>
    /// <b>Con currículum autorado, esto MANDA sobre lo que el editor escribió</b> en
    /// <c>courseLessonCount</c> y <c>courseDurationMinutes</c> — y su descripción en el schema
    /// lo dice. Son el mismo hecho contado dos veces, y dos fuentes para un mismo hecho
    /// derivan: la tarjeta anunciaría «12 lecciones» sobre un temario de nueve.
    ///
    /// <para>Es distinto de <c>eventPriceFrom</c>, que SÍ convive con las localidades: ahí son
    /// hechos distintos (precio de exhibición vs. cobro real) y la fuente de Eventos lo razona
    /// con todas las letras. Aquí no lo son.</para>
    ///
    /// <para>Sin currículum, lo declarado es lo único que hay y se respeta: un curso puede
    /// anunciarse antes de que su temario esté cargado.</para>
    /// </remarks>
    public static (int LessonCount, int DurationMinutes) Aggregate(
        IReadOnlyList<AuthoredModule> modules,
        int declaredLessonCount,
        int declaredDurationMinutes)
    {
        if (modules.Count == 0)
        {
            return (Math.Max(0, declaredLessonCount), Math.Max(0, declaredDurationMinutes));
        }

        return (
            modules.Sum(m => m.Lessons.Count),
            modules.Sum(m => m.Lessons.Sum(l => l.DurationMinutes)));
    }

    /// <summary>
    /// El id que fijó el editor, o uno derivado y estable. Nunca dos iguales en un curso.
    /// </summary>
    /// <remarks>
    /// El desempate va con un sufijo numérico y no descarta el duplicado: dos módulos que se
    /// llamen igual son raros pero legítimos («Práctica», «Práctica»), y perder el segundo
    /// sería perder media clase por un nombre repetido.
    /// </remarks>
    private static string ResolveId(string? authored, string derived, HashSet<string> used)
    {
        var baseId = Slugify(authored);
        if (baseId.Length == 0)
        {
            baseId = derived;
        }

        if (baseId.Length == 0)
        {
            baseId = "x";
        }

        var candidate = baseId;
        var suffix = 2;
        while (!used.Add(candidate))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        return candidate;
    }

    /// <summary>
    /// Texto → identificador: minúsculas, dígitos y guiones, sin acentos.
    /// </summary>
    /// <remarks>
    /// Lista BLANCA y no negra: estos ids acaban siendo claves del store y segmentos de URL, y
    /// una lista negra siempre se queda corta. Los acentos se descomponen antes de filtrar,
    /// para que «Introducción» dé <c>introduccion</c> y no <c>introduccin</c>.
    /// </remarks>
    internal static string Slugify(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var normalized = raw.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        return builder.ToString().Trim('-');
    }

    private static string ResourceKind(string url)
    {
        var path = url.Split('?', '#')[0];
        var dot = path.LastIndexOf('.');
        if (dot < 0 || dot == path.Length - 1)
        {
            return "link";
        }

        return path[(dot + 1)..].ToLowerInvariant() switch
        {
            "pdf" => "pdf",
            "csv" or "xlsx" or "xls" => "dataset",
            "zip" or "rar" or "7z" => "archive",
            "doc" or "docx" or "txt" or "md" => "document",
            "png" or "jpg" or "jpeg" or "webp" or "svg" => "image",
            _ => "link",
        };
    }

    private static CourseContentIssue Warn(string message) => new(CourseContentIssueLevel.Warning, message);
}

/// <summary>Un recurso tal como llega del enlace que puso el editor.</summary>
public sealed record AuthoredResourceDraft(string? Title, string? Url);

/// <summary>Una lección tal como sale del bloque, sin normalizar.</summary>
public sealed record AuthoredLessonDraft(
    string? Id,
    string? Title,
    int DurationMinutes,
    string? VideoUrl,
    string? Body,
    IReadOnlyList<AuthoredResourceDraft>? Resources,
    bool IsPreview);

/// <summary>Un módulo tal como sale del bloque, sin normalizar.</summary>
public sealed record AuthoredModuleDraft(string? Id, string? Title, IReadOnlyList<AuthoredLessonDraft> Lessons);
