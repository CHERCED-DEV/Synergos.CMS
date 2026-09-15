namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Gravedad de lo que se encontró al leer un <c>professionalPage</c>.</summary>
public enum ProfessionalContentIssueLevel
{
    /// <summary>Se sirvió igual, con un valor derivado o recortado.</summary>
    Warning,

    /// <summary>No se pudo servir ese dato, o el profesional entero se omite.</summary>
    Error,
}

/// <summary>Algo que el editor tiene que arreglar, con el texto que lo dice.</summary>
public sealed record ProfessionalContentIssue(ProfessionalContentIssueLevel Level, string Message);

/// <summary>Lo construido más lo que hubo que señalar por el camino.</summary>
public sealed record ProfessionalContentResult<T>(T Value, IReadOnlyList<ProfessionalContentIssue> Issues);

/// <summary>
/// Las reglas de leer un <c>professionalPage</c>: qué se acepta, qué se deriva y qué hace que
/// un profesional no se pueda servir. Lógica PURA — no conoce Umbraco, así que se prueba sin
/// levantar un contexto.
/// </summary>
/// <remarks>
/// Calca <see cref="CourseContentRules"/> y <see cref="StayContentRules"/>: la fuente recorre el
/// árbol y ésta decide. La separación es lo que hace que las decisiones del editor se puedan
/// probar sin un <c>IPublishedContent</c>.
/// </remarks>
public static class ProfessionalContentRules
{
    /// <summary>Duración de cita cuando el editor no la escribió.</summary>
    /// <remarks>
    /// <b>No es una constante nueva: es la que ya estaba.</b>
    /// <c>HttpClinicalSchedulingService</c> calcula el fin de la ventana con
    /// <c>doctor.SlotMinutes &gt; 0 ? doctor.SlotMinutes : 30</c> desde la HU #25. Escribir aquí
    /// otro número dejaría la cita durando una cosa en el CMS y otra contra el orquestador.
    /// </remarks>
    public const int DefaultSlotMinutes = 30;

    /// <summary>El vocabulario del desplegable de días, en orden de semana.</summary>
    /// <remarks>
    /// Va en el mismo orden que <c>DTSelectWorkingDays</c>. Es lo que ordena la respuesta: un
    /// desplegable múltiple devuelve los valores en el orden en que el editor los marcó, y dos
    /// profesionales con los mismos días saldrían con listas distintas.
    /// </remarks>
    private static readonly (string Valor, DayOfWeek Dia)[] Dias =
    [
        ("lunes", DayOfWeek.Monday),
        ("martes", DayOfWeek.Tuesday),
        ("miercoles", DayOfWeek.Wednesday),
        ("jueves", DayOfWeek.Thursday),
        ("viernes", DayOfWeek.Friday),
        ("sabado", DayOfWeek.Saturday),
        ("domingo", DayOfWeek.Sunday),
    ];

    /// <summary>
    /// Los días en que atiende, deduplicados y en orden de semana.
    /// </summary>
    /// <remarks>
    /// <b>Sin días no se inventa una semana laboral.</b> Un profesional al que nadie le marcó
    /// días sale con la lista vacía y se avisa: rellenarlo con lunes-viernes ofrecería horas
    /// que nadie dijo que existieran, y una hora ofrecida es alguien presentándose en un
    /// consultorio (regla de <c>feedback_gethashcode_is_not_a_seed</c> — sin dato, se dice que
    /// no hay dato).
    /// </remarks>
    public static ProfessionalContentResult<IReadOnlyList<DayOfWeek>> ParseWorkingDays(
        string slug, IEnumerable<string>? raw)
    {
        var issues = new List<ProfessionalContentIssue>();
        var marcados = new HashSet<DayOfWeek>();

        foreach (var valor in raw ?? Enumerable.Empty<string>())
        {
            var v = (valor ?? string.Empty).Trim().ToLowerInvariant();
            if (v.Length == 0)
            {
                continue;
            }

            var match = Dias.FirstOrDefault(d => string.Equals(d.Valor, v, StringComparison.Ordinal));
            if (match.Valor is null)
            {
                issues.Add(Error(
                    $"professionalPage slug='{slug}' tiene un día de atención desconocido: '{valor}'. "
                    + "Se ignora. Valores admitidos: "
                    + string.Join(", ", Dias.Select(d => d.Valor)) + "."));
                continue;
            }

            marcados.Add(match.Dia);
        }

        var dias = Dias.Where(d => marcados.Contains(d.Dia)).Select(d => d.Dia).ToList();

        if (dias.Count == 0)
        {
            issues.Add(Warn(
                $"professionalPage slug='{slug}' no tiene ningún día de atención marcado. Sale en "
                + "el directorio, pero no se le puede agendar."));
        }

        return new ProfessionalContentResult<IReadOnlyList<DayOfWeek>>(dias, issues);
    }

    /// <summary>
    /// La franja de atención, o <c>(0, 0)</c> —«no hay franja»— si lo escrito no es una.
    /// </summary>
    /// <remarks>
    /// <b>Una franja imposible no se arregla adivinando, se anula.</b> Si el fin no es posterior
    /// al inicio, o alguna está fuera de 0-23, lo que hay es un dato que el editor tiene que
    /// corregir; servir «8 a 17» porque suena razonable sería exactamente el respaldo derivado
    /// que tapa un hueco con algo plausible (<c>feedback_a_derived_fallback_must_never_overwrite
    /// _what_arrived</c>). Con <c>(0, 0)</c> no se ofrece ninguna hora y el log lo dice.
    /// </remarks>
    public static ProfessionalContentResult<(int Start, int End)> ParseSlotWindow(
        string slug, int start, int end)
    {
        if (start == 0 && end == 0)
        {
            // El editor no la escribió. Es el mismo caso que «sin días»: no hay franja y no se
            // inventa, pero tampoco es un dato equivocado, así que no es un error.
            return new ProfessionalContentResult<(int, int)>(
                (0, 0),
                [Warn($"professionalPage slug='{slug}' no tiene franja de atención. Sale en el "
                      + "directorio, pero no se le puede agendar.")]);
        }

        if (start is < 0 or > 23 || end is < 0 or > 23 || end <= start)
        {
            return new ProfessionalContentResult<(int, int)>(
                (0, 0),
                [Error($"professionalPage slug='{slug}' tiene una franja imposible "
                       + $"({start} a {end}). Se sirve SIN franja: no se le puede agendar hasta "
                       + "que se corrija. Las dos horas van entre 0 y 23 y la de cierre tiene "
                       + "que ser mayor que la de inicio.")]);
        }

        return new ProfessionalContentResult<(int, int)>((start, end), []);
    }

    /// <summary>La duración de la cita, con el default que ya usa el cliente del orquestador.</summary>
    public static ProfessionalContentResult<int> ParseSlotMinutes(string slug, int raw)
    {
        if (raw == 0)
        {
            return new ProfessionalContentResult<int>(DefaultSlotMinutes, []);
        }

        if (raw < 0)
        {
            return new ProfessionalContentResult<int>(
                DefaultSlotMinutes,
                [Error($"professionalPage slug='{slug}' tiene una duración de cita negativa "
                       + $"({raw}). Se sirve con {DefaultSlotMinutes} minutos.")]);
        }

        return new ProfessionalContentResult<int>(raw, []);
    }

    /// <summary>
    /// Si admite pacientes nuevos. <b><c>null</c> es «no consta», y es un valor, no un hueco.</b>
    /// </summary>
    /// <remarks>
    /// <b>Por eso el campo es un desplegable de dos valores y no un <c>Umbraco.TrueFalse</c>.</b>
    /// Un booleano del backoffice vale <c>false</c> sin que nadie lo decida, así que todo
    /// profesional que el editor no tocara diría «no admite pacientes nuevos» — y eso manda a
    /// alguien a buscar médico a otra parte teniendo uno que sí recibe. Es
    /// <c>feedback_an_omitted_key_can_be_an_assertion</c> con el default puesto en el schema en
    /// vez de en el normalizador.
    /// </remarks>
    public static ProfessionalContentResult<bool?> ParseAcceptingPatients(string slug, string? raw)
    {
        var v = (raw ?? string.Empty).Trim().ToLowerInvariant();

        return v switch
        {
            "" => new ProfessionalContentResult<bool?>(null, []),
            "si" or "sí" => new ProfessionalContentResult<bool?>(true, []),
            "no" => new ProfessionalContentResult<bool?>(false, []),
            _ => new ProfessionalContentResult<bool?>(
                null,
                [Error($"professionalPage slug='{slug}' tiene "
                       + $"professionalAcceptingPatients='{raw}', que no es ni 'si' ni 'no'. Se "
                       + "sirve como «no consta»: decir que no admite pacientes por una errata "
                       + "sería peor que no decir nada.")]),
        };
    }

    /// <summary>
    /// Los slugs repetidos del directorio entero.
    /// </summary>
    /// <remarks>
    /// <b>Dos profesionales con el mismo slug son el mismo SUJETO para <c>Api.Booking</c></b>, así
    /// que las citas de uno caerían sobre el recurso del otro sin que nada fallara. Sólo se ve
    /// con el directorio completo en la mano, igual que las colisiones de tipo de habitación de
    /// <see cref="StayContentRules"/>, así que va aquí y no dentro de la proyección de un nodo.
    /// </remarks>
    public static IReadOnlyList<ProfessionalContentIssue> FindSlugCollisions(
        IEnumerable<string> slugs)
        => slugs
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => Error(
                $"El slug '{g.Key}' está en {g.Count()} professionalPage. Para la agenda son el "
                + "MISMO profesional, así que las citas de uno caen sobre el otro. Renómbralos."))
            .ToList();

    private static ProfessionalContentIssue Warn(string message)
        => new(ProfessionalContentIssueLevel.Warning, message);

    private static ProfessionalContentIssue Error(string message)
        => new(ProfessionalContentIssueLevel.Error, message);
}
