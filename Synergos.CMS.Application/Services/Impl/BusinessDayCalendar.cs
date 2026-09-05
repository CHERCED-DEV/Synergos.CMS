using System.Collections.Concurrent;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Días hábiles colombianos: fines de semana fuera, y los 18 festivos de la Ley 51 de 1983.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra</b> (#77). El schema le promete al editor «cuántos días
/// <b>hábiles</b> tarda la respuesta» —está escrito así en la descripción de
/// <c>tramiteEstimatedDays</c>— y el expediente contaba días calendario. Un trámite de 15 días
/// radicado un lunes vencía seis días antes de lo autorado, sin contar un solo festivo.</para>
///
/// <para><b>Por qué vive acá y no en <c>Synergos.Core</c>.</b> La épica de Gobierno lo pedía en
/// Core, y Core es «el vocabulario»: <c>TimeWindow</c> sabe de intervalos, no de Colombia. Un
/// calendario con la regla Emiliani es un sustantivo de negocio, y meterlo ahí sería lo mismo que
/// §0.B.12 prohíbe dentro de una capacidad. Vive donde tiene su primer consumidor, y sube el día
/// que aparezca el segundo (§0.B.17) — que es la regla que hizo esperar a <c>Shared</c> hasta
/// seis y a <c>Bff.Core</c> hasta dos.</para>
///
/// <para><b>Los festivos se CALCULAN, no se listan.</b> Una tabla a mano se acaba el año que
/// nadie la renueva, y falla en silencio: el cálculo simplemente cuenta un día de más. Los 18
/// salen de tres reglas —seis fechas fijas, siete corridas al lunes siguiente por la Ley Emiliani,
/// y cinco colgadas de la Pascua— y la Pascua es determinista.</para>
///
/// <para><b>Lo que NO cubre, dicho para que nadie lo suponga:</b> festivos locales o de sector
/// (un día cívico municipal), y el horario de radicación —un documento radicado a las 23:59 cuenta
/// como ese día hábil acá—. Lo primero necesitaría que la entidad los declarara; lo segundo, que
/// alguien decida a qué hora cierra la ventanilla.</para>
/// </remarks>
public static class BusinessDayCalendar
{
    /// <summary>
    /// Los festivos de un año, calculados una sola vez.
    /// </summary>
    /// <remarks>
    /// Se cachea porque el cálculo se pide por cada expediente de cada listado, y la respuesta de
    /// un año no cambia nunca — es aritmética sobre el calendario, no un dato que alguien edite.
    /// </remarks>
    private static readonly ConcurrentDictionary<int, IReadOnlySet<DateOnly>> _porAnio = new();

    /// <summary>Los 18 festivos colombianos de <paramref name="year"/>.</summary>
    public static IReadOnlySet<DateOnly> Holidays(int year)
        => _porAnio.GetOrAdd(year, Calcular);

    /// <summary>Si es día hábil: ni sábado, ni domingo, ni festivo.</summary>
    public static bool IsBusinessDay(DateOnly day)
        => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
           && !Holidays(day.Year).Contains(day);

    /// <summary>
    /// El día hábil que resulta de sumar <paramref name="days"/> hábiles a <paramref name="from"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Cuenta desde el día siguiente</b>, que es como cuenta un término: un plazo de «un
    /// día hábil» dado un lunes vence el martes, no el mismo lunes. Con <c>days = 0</c> devuelve
    /// el día de partida sin moverlo — el trámite que no promete plazo.</para>
    ///
    /// <para>Días negativos no tienen sentido en un término y se tratan como cero: un plazo hacia
    /// atrás sería un dato mal autorado, y hacerlo retroceder convertiría un error de captura en
    /// un expediente vencido el día que se radica.</para>
    /// </remarks>
    public static DateOnly AddBusinessDays(DateOnly from, int days)
    {
        if (days <= 0) return from;

        var cursor = from;
        for (var restantes = days; restantes > 0; restantes--)
        {
            do { cursor = cursor.AddDays(1); }
            while (!IsBusinessDay(cursor));
        }

        return cursor;
    }

    /// <summary>
    /// Cuántos días hábiles hay entre <paramref name="from"/> y <paramref name="to"/>.
    /// </summary>
    /// <remarks>
    /// Positivo si <paramref name="to"/> está adelante, negativo si ya pasó, y <b>cero el mismo
    /// día del vencimiento</b> — que es el último día para responder, no el primero de mora. Esa
    /// frontera es la que decide si un expediente aparece vencido en la cola del funcionario, así
    /// que va escrita y con test.
    /// </remarks>
    public static int BusinessDaysBetween(DateOnly from, DateOnly to)
    {
        if (from == to) return 0;

        var adelante = to > from;
        var (inicio, fin) = adelante ? (from, to) : (to, from);

        var habiles = 0;
        for (var cursor = inicio.AddDays(1); cursor <= fin; cursor = cursor.AddDays(1))
        {
            if (IsBusinessDay(cursor)) habiles++;
        }

        return adelante ? habiles : -habiles;
    }

    /// <summary>Los tres grupos de festivos, según de dónde sale su fecha.</summary>
    private static IReadOnlySet<DateOnly> Calcular(int year)
    {
        var pascua = Easter(year);

        var festivos = new HashSet<DateOnly>
        {
            // Fijos: caen donde caen, aunque sea domingo.
            new(year, 1, 1),    // Año Nuevo
            new(year, 5, 1),    // Día del Trabajo
            new(year, 7, 20),   // Independencia
            new(year, 8, 7),    // Batalla de Boyacá
            new(year, 12, 8),   // Inmaculada Concepción
            new(year, 12, 25),  // Navidad

            // Semana Santa: colgados de la Pascua y NO se corren.
            pascua.AddDays(-3), // Jueves Santo
            pascua.AddDays(-2), // Viernes Santo

            // Colgados de la Pascua Y corridos al lunes (Ley Emiliani).
            pascua.AddDays(43), // Ascensión
            pascua.AddDays(64), // Corpus Christi
            pascua.AddDays(71), // Sagrado Corazón
        };

        // Emiliani: si no cae lunes, se traslada al lunes siguiente.
        foreach (var fecha in new DateOnly[]
                 {
                     new(year, 1, 6),    // Reyes Magos
                     new(year, 3, 19),   // San José
                     new(year, 6, 29),   // San Pedro y San Pablo
                     new(year, 8, 15),   // Asunción
                     new(year, 10, 12),  // Día de la Raza
                     new(year, 11, 1),   // Todos los Santos
                     new(year, 11, 11),  // Independencia de Cartagena
                 })
        {
            festivos.Add(ProximoLunes(fecha));
        }

        return festivos;
    }

    /// <summary>El propio día si ya es lunes; si no, el lunes siguiente.</summary>
    private static DateOnly ProximoLunes(DateOnly d)
        => d.AddDays((7 - ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7) % 7);

    /// <summary>
    /// El domingo de Pascua, por el algoritmo gregoriano anónimo (Meeus/Jones/Butcher).
    /// </summary>
    /// <remarks>
    /// Cinco de los dieciocho festivos cuelgan de esta fecha. Es aritmética pura y vale para
    /// cualquier año del calendario gregoriano, así que no hay tabla que envejezca.
    /// </remarks>
    private static DateOnly Easter(int year)
    {
        var a = year % 19;
        var b = year / 100;
        var c = year % 100;
        var d = b / 4;
        var e = b % 4;
        var f = (b + 8) / 25;
        var g = (b - f + 1) / 3;
        var h = ((19 * a) + b - d - g + 15) % 30;
        var i = c / 4;
        var k = c % 4;
        var l = (32 + (2 * e) + (2 * i) - h - k) % 7;
        var m = (a + (11 * h) + (22 * l)) / 451;
        var mes = (h + l - (7 * m) + 114) / 31;
        var dia = ((h + l - (7 * m) + 114) % 31) + 1;

        return new DateOnly(year, mes, dia);
    }
}
