using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El calendario hábil colombiano (#77).
/// </summary>
/// <remarks>
/// <para><b>Los festivos se calculan, así que lo que hay que probar es el CÁLCULO</b>, no una
/// lista. Se comprueba contra dos años completos con las fechas reales: si la regla Emiliani o el
/// desplazamiento de Pascua estuvieran mal, el conteo de días hábiles fallaría en silencio — que
/// es exactamente el modo de fallo del defecto que esto cierra.</para>
/// </remarks>
public sealed class BusinessDayCalendarTests
{
    // Los 18 de 2026, verificados contra el calendario oficial.
    private static readonly string[] Festivos2026 =
    {
        "2026-01-01", "2026-01-12", "2026-03-23", "2026-04-02", "2026-04-03", "2026-05-01",
        "2026-05-18", "2026-06-08", "2026-06-15", "2026-06-29", "2026-07-20", "2026-08-07",
        "2026-08-17", "2026-10-12", "2026-11-02", "2026-11-16", "2026-12-08", "2026-12-25",
    };

    // Los 18 de 2027. Otro año a propósito: 2026 solo podría pasar por coincidencia.
    private static readonly string[] Festivos2027 =
    {
        "2027-01-01", "2027-01-11", "2027-03-22", "2027-03-25", "2027-03-26", "2027-05-01",
        "2027-05-10", "2027-05-31", "2027-06-07", "2027-07-05", "2027-07-20", "2027-08-07",
        "2027-08-16", "2027-10-18", "2027-11-01", "2027-11-15", "2027-12-08", "2027-12-25",
    };

    private static DateOnly D(string iso) => DateOnly.Parse(iso, System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(2026)]
    [InlineData(2027)]
    public void Cada_anio_tiene_dieciocho_festivos(int anio)
        => Assert.Equal(18, BusinessDayCalendar.Holidays(anio).Count);

    [Fact]
    public void Los_festivos_de_2026_son_los_del_calendario_oficial()
        => Assert.Equal(
            Festivos2026.Select(D).OrderBy(d => d),
            BusinessDayCalendar.Holidays(2026).OrderBy(d => d));

    [Fact]
    public void Los_festivos_de_2027_tambien()
        => Assert.Equal(
            Festivos2027.Select(D).OrderBy(d => d),
            BusinessDayCalendar.Holidays(2027).OrderBy(d => d));

    /// <summary>La regla Emiliani corre al lunes; Semana Santa NO se corre.</summary>
    /// <remarks>
    /// Son las dos reglas que se confunden entre sí, y confundirlas mueve cinco festivos de sitio.
    /// Reyes cae martes en 2026 y se va al lunes 12; Viernes Santo cae viernes y se queda.
    /// </remarks>
    [Fact]
    public void Emiliani_corre_al_lunes_y_la_Semana_Santa_no()
    {
        Assert.Contains(D("2026-01-12"), BusinessDayCalendar.Holidays(2026));   // Reyes, corrido
        Assert.DoesNotContain(D("2026-01-06"), BusinessDayCalendar.Holidays(2026));

        Assert.Contains(D("2026-04-03"), BusinessDayCalendar.Holidays(2026));   // Viernes Santo, viernes
    }

    /// <summary>Un festivo que ya cae lunes no se mueve.</summary>
    [Fact]
    public void Un_festivo_que_ya_cae_lunes_se_queda()
    {
        // 12-oct-2026 es lunes: el Día de la Raza no se corre al 19.
        Assert.Contains(D("2026-10-12"), BusinessDayCalendar.Holidays(2026));
        Assert.DoesNotContain(D("2026-10-19"), BusinessDayCalendar.Holidays(2026));
    }

    // ── Día hábil ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-07", true)]    // lunes normal
    [InlineData("2026-09-12", false)]   // sábado
    [InlineData("2026-09-13", false)]   // domingo
    [InlineData("2026-12-25", false)]   // Navidad, viernes
    [InlineData("2026-01-12", false)]   // Reyes corrido al lunes
    public void Un_dia_es_habil_si_no_es_fin_de_semana_ni_festivo(string dia, bool habil)
        => Assert.Equal(habil, BusinessDayCalendar.IsBusinessDay(D(dia)));

    // ── El defecto que esto cierra ───────────────────────────────────────────

    /// <summary>
    /// Quince días hábiles desde un lunes vencen SEIS días calendario más tarde.
    /// </summary>
    /// <remarks>
    /// Es el ejemplo exacto del defecto: el sistema tomaba el 22 de septiembre y la ficha prometía
    /// el 28. Sin contar un solo festivo — la semana del ejemplo no tiene ninguno.
    /// </remarks>
    [Fact]
    public void Quince_habiles_desde_un_lunes_no_son_quince_calendario()
    {
        var radicado = D("2026-09-07");                                   // lunes

        Assert.Equal(D("2026-09-28"), BusinessDayCalendar.AddBusinessDays(radicado, 15));
        Assert.NotEqual(radicado.AddDays(15), BusinessDayCalendar.AddBusinessDays(radicado, 15));
    }

    /// <summary>Un festivo dentro del plazo lo empuja un día más.</summary>
    [Fact]
    public void Un_festivo_en_medio_alarga_el_termino()
    {
        // Del 5-oct-2026 (lunes), cinco hábiles caen el viernes 9 si no hubiera festivos.
        // El 12 es Día de la Raza, así que a diez hábiles se nota: cae el 20, no el 19.
        Assert.Equal(D("2026-10-20"), BusinessDayCalendar.AddBusinessDays(D("2026-10-05"), 10));
    }

    [Fact] // sin plazo, el trámite no promete nada y la fecha no se mueve.
    public void Cero_dias_no_mueve_la_fecha()
        => Assert.Equal(D("2026-09-07"), BusinessDayCalendar.AddBusinessDays(D("2026-09-07"), 0));

    /// <summary>Un plazo negativo se trata como cero, no hacia atrás.</summary>
    /// <remarks>
    /// Un número negativo en la ficha es un error de captura; hacer retroceder el vencimiento
    /// convertiría ese error en un expediente vencido el día que se radica.
    /// </remarks>
    [Fact]
    public void Un_plazo_negativo_no_retrocede()
        => Assert.Equal(D("2026-09-07"), BusinessDayCalendar.AddBusinessDays(D("2026-09-07"), -3));

    // ── La distancia, y su frontera ──────────────────────────────────────────

    [Fact]
    public void Entre_lunes_y_el_viernes_de_esa_semana_hay_cuatro_habiles()
        => Assert.Equal(4, BusinessDayCalendar.BusinessDaysBetween(D("2026-09-07"), D("2026-09-11")));

    [Fact] // el fin de semana no cuenta: de viernes a lunes hay uno.
    public void El_fin_de_semana_no_cuenta()
        => Assert.Equal(1, BusinessDayCalendar.BusinessDaysBetween(D("2026-09-11"), D("2026-09-14")));

    /// <summary>El día del vencimiento es CERO, no menos uno.</summary>
    /// <remarks>
    /// Es el último día para responder, no el primero de mora — y es la frontera que decide si un
    /// expediente aparece vencido en la cola del funcionario.
    /// </remarks>
    [Fact]
    public void El_dia_del_vencimiento_quedan_cero_dias()
        => Assert.Equal(0, BusinessDayCalendar.BusinessDaysBetween(D("2026-09-28"), D("2026-09-28")));

    [Fact] // pasado el plazo, negativo.
    public void Despues_del_vencimiento_la_cuenta_es_negativa()
        => Assert.Equal(-2, BusinessDayCalendar.BusinessDaysBetween(D("2026-09-30"), D("2026-09-28")));
}
