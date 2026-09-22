using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Con cuánta fuerza se queja una regla del contenido de un equipo.</summary>
public enum EquipmentContentIssueLevel
{
    /// <summary>El dato se sirve, corregido o vacío.</summary>
    Warning,

    /// <summary>El equipo NO se sirve.</summary>
    Error,
}

/// <summary>Un reparo sobre el contenido de un <c>equipmentPage</c>.</summary>
/// <param name="Level">Si el equipo se sirve o no.</param>
/// <param name="Message">Qué pasó, con el slug dentro para poder buscarlo.</param>
public sealed record EquipmentContentIssue(EquipmentContentIssueLevel Level, string Message);

/// <summary>Un valor ya normalizado y los reparos que salieron al normalizarlo.</summary>
/// <typeparam name="T">El tipo del valor.</typeparam>
/// <param name="Value">El valor servible.</param>
/// <param name="Issues">Lo que hubo que corregir o lo que impide servirlo.</param>
public sealed record EquipmentContentResult<T>(T Value, IReadOnlyList<EquipmentContentIssue> Issues);

/// <summary>
/// Las reglas del contenido de un equipo, PURAS: no saben qué es Umbraco (ADR 0002).
/// </summary>
/// <remarks>
/// <para>Es el mismo corte que <see cref="ProfessionalContentRules"/> y
/// <c>SocialContentRules</c>: la fuente toca el árbol de contenido y las decisiones viven acá,
/// donde un test las ejercita sin levantar un CMS.</para>
///
/// <para><b>Lo que esta clase NO hace es rellenar.</b> Un tramo de tarifa ilegible se descarta y
/// se dice; no se «corrige» a la tarifa base, porque un precio inventado es exactamente el
/// defecto del #123 con otra cara — sale plausible y nadie lo mira hasta que alguien paga.</para>
/// </remarks>
public static class EquipmentContentRules
{
    /// <summary>Lo que se usa cuando el editor deja el mínimo vacío: un día.</summary>
    public const int MinDaysFloor = 1;

    private static readonly EquipmentContentIssue[] Ninguno = Array.Empty<EquipmentContentIssue>();

    /// <summary>
    /// Acota el mínimo y el máximo de días contra el tope del despliegue.
    /// </summary>
    /// <remarks>
    /// <b>El tope existe por una razón del mundo real, no por prudencia:</b> la garantía se
    /// RETIENE con una autorización de pago, y una autorización no dura para siempre. Un
    /// alquiler más largo que eso llegaría al día de la devolución con la retención ya vencida
    /// y sin nada que anular ni capturar — o sea con la garantía perdida y sin que nada falle.
    /// Por eso se acota al publicar y además se rechaza al reservar
    /// (<c>alquiler.window_too_long</c>): acotar acá arregla lo que el editor escribió, y
    /// rechazar allá arregla lo que pida quien llama por la API.
    /// </remarks>
    /// <param name="slug">Para poder buscar el equipo en el log.</param>
    /// <param name="rawMin">Lo que el editor puso como mínimo; 0 o vacío es «un día».</param>
    /// <param name="rawMax">Lo que puso como máximo; 0 o vacío es «el tope del despliegue».</param>
    /// <param name="deploymentMaxDays">El tope de <c>Synergos:Alquiler:MaxRentalDays</c>.</param>
    /// <returns>El par ya acotado, con lo que hubo que corregir.</returns>
    public static EquipmentContentResult<(int Min, int Max)> ParseDayBounds(
        string slug, int rawMin, int rawMax, int deploymentMaxDays)
    {
        var issues = new List<EquipmentContentIssue>();
        var tope = deploymentMaxDays > 0 ? deploymentMaxDays : int.MaxValue;

        var min = rawMin > 0 ? rawMin : MinDaysFloor;
        var max = rawMax > 0 ? rawMax : tope;

        if (max > tope)
        {
            issues.Add(new EquipmentContentIssue(EquipmentContentIssueLevel.Warning,
                $"equipmentPage slug='{slug}': el máximo de {rawMax} días supera el tope del "
                + $"despliegue ({tope}); se sirve {tope}. Una autorización de garantía no dura más."));
            max = tope;
        }

        if (min > max)
        {
            issues.Add(new EquipmentContentIssue(EquipmentContentIssueLevel.Warning,
                $"equipmentPage slug='{slug}': el mínimo ({min}) supera al máximo ({max}); "
                + "se sirve el mínimo igual al máximo."));
            min = max;
        }

        return new EquipmentContentResult<(int, int)>((min, max), issues.Count == 0 ? Ninguno : issues);
    }

    /// <summary>
    /// Normaliza los tramos de tarifa: descarta los ilegibles y los deja ordenados.
    /// </summary>
    /// <remarks>
    /// <para><b>Un tramo con día 0 o menos se DESCARTA, no se sube a 1.</b> Subirlo lo pondría a
    /// competir con la tarifa base por el mismo día, y cuál gana dependería del orden en que el
    /// editor los arrastró — un desempate que nadie tomó, que es el #131.</para>
    ///
    /// <para><b>Y dos tramos con el mismo <c>MinDays</c> también se descartan los dos</b>, por lo
    /// mismo: con uno de los dos servido, republicar cambiaría el precio sin que nadie lo
    /// decidiera. Descartar los dos deja el equipo cobrando su tarifa base, que es la verdad
    /// conocida, y lo dice en el log.</para>
    /// </remarks>
    /// <param name="slug">Para poder buscar el equipo en el log.</param>
    /// <param name="crudos">Los tramos tal como salieron del Block List.</param>
    /// <returns>Los tramos servibles, ordenados por <c>MinDays</c>.</returns>
    public static EquipmentContentResult<IReadOnlyList<EquipmentRate>> ParseRates(
        string slug, IEnumerable<EquipmentRate>? crudos)
    {
        if (crudos is null)
        {
            return new EquipmentContentResult<IReadOnlyList<EquipmentRate>>(
                Array.Empty<EquipmentRate>(), Ninguno);
        }

        var issues = new List<EquipmentContentIssue>();
        var vivos = new List<EquipmentRate>();

        foreach (var r in crudos)
        {
            if (r.MinDays < MinDaysFloor)
            {
                issues.Add(new EquipmentContentIssue(EquipmentContentIssueLevel.Warning,
                    $"equipmentPage slug='{slug}': el tramo '{r.Code}' empieza en {r.MinDays} días; "
                    + "se descarta. Competiría con la tarifa base por el mismo día."));
                continue;
            }

            if (r.PerDay < 0m)
            {
                issues.Add(new EquipmentContentIssue(EquipmentContentIssueLevel.Warning,
                    $"equipmentPage slug='{slug}': el tramo '{r.Code}' tiene valor negativo; se descarta."));
                continue;
            }

            vivos.Add(r);
        }

        var repetidos = vivos.GroupBy(r => r.MinDays).Where(g => g.Count() > 1).ToList();
        foreach (var g in repetidos)
        {
            issues.Add(new EquipmentContentIssue(EquipmentContentIssueLevel.Warning,
                $"equipmentPage slug='{slug}': {g.Count()} tramos empiezan en {g.Key} días "
                + $"({string.Join(", ", g.Select(r => r.Code))}); se descartan todos. "
                + "Servir uno dejaría que el orden del editor decidiera el precio."));
        }

        var repetidosDias = repetidos.Select(g => g.Key).ToHashSet();
        var servibles = vivos
            .Where(r => !repetidosDias.Contains(r.MinDays))
            .OrderBy(r => r.MinDays)
            .ToList();

        return new EquipmentContentResult<IReadOnlyList<EquipmentRate>>(
            servibles, issues.Count == 0 ? Ninguno : issues);
    }

    /// <summary>
    /// Dos equipos con el mismo slug: sólo se ve con el catálogo entero en la mano.
    /// </summary>
    /// <param name="slugs">Los identificadores ya proyectados.</param>
    /// <returns>Un reparo por slug repetido.</returns>
    public static IReadOnlyList<EquipmentContentIssue> FindSlugCollisions(IEnumerable<string> slugs)
    {
        var repetidos = slugs
            .GroupBy(s => s, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (repetidos.Count == 0)
        {
            return Ninguno;
        }

        return repetidos
            .Select(s => new EquipmentContentIssue(EquipmentContentIssueLevel.Error,
                $"equipmentPage: el slug '{s}' está en más de un equipo. La reserva iría al que "
                + "el árbol devuelva primero, que no lo decide nadie."))
            .ToList();
    }
}
