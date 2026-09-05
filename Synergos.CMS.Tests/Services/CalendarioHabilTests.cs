using Synergos.CMS.Application.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El calendario de días hábiles de Colombia (defecto #77).
/// </summary>
/// <remarks>
/// <para>Los festivos <b>se calculan</b>, así que lo que hay que probar no es una tabla: es que el
/// cálculo coincide con el calendario real, año por año. Las fechas de abajo son las oficiales, no
/// las que devuelve el código — comprobar el cálculo contra sí mismo es exactamente el defecto que
/// el ticket señala en los tests que ya había.</para>
/// </remarks>
public sealed class CalendarioHabilTests
{
    // ── Los dieciocho, contra el calendario real ────────────────────────────

    /// <summary>
    /// Los festivos de 2026, uno por uno.
    /// </summary>
    /// <remarks>
    /// 2026 es el año del ejemplo del ticket y trae los tres casos: los que no se mueven, los que
    /// la regla Emiliani corre al lunes, y los cinco que cuelgan de la Pascua (5 de abril).
    /// </remarks>
    [Theory]
    // Fijos — la ley NO los corre.
    [InlineData(2026, 1, 1)]     // Año Nuevo (jueves, se queda)
    [InlineData(2026, 5, 1)]     // Trabajo (viernes)
    [InlineData(2026, 7, 20)]    // Independencia (lunes)
    [InlineData(2026, 8, 7)]     // Boyacá (viernes)
    [InlineData(2026, 12, 8)]    // Inmaculada (martes, se queda)
    [InlineData(2026, 12, 25)]   // Navidad (viernes)
    // Emiliani — corridos al lunes siguiente.
    [InlineData(2026, 1, 12)]    // Reyes: 6-ene es martes
    [InlineData(2026, 3, 23)]    // San José: 19-mar es jueves
    [InlineData(2026, 6, 29)]    // San Pedro: 29-jun YA es lunes, no se mueve
    [InlineData(2026, 8, 17)]    // Asunción: 15-ago es sábado
    [InlineData(2026, 10, 12)]   // Raza: 12-oct YA es lunes
    [InlineData(2026, 11, 2)]    // Todos los Santos: 1-nov es domingo
    [InlineData(2026, 11, 16)]   // Cartagena: 11-nov es miércoles
    // De Pascua (5 de abril de 2026).
    [InlineData(2026, 4, 2)]     // Jueves Santo — NO se corre
    [InlineData(2026, 4, 3)]     // Viernes Santo — NO se corre
    [InlineData(2026, 5, 18)]    // Ascensión
    [InlineData(2026, 6, 8)]     // Corpus Christi
    [InlineData(2026, 6, 15)]    // Sagrado Corazón
    public void Los_festivos_de_2026_no_son_habiles(int año, int mes, int dia)
        => Assert.False(CalendarioHabil.EsHabil(new DateOnly(año, mes, dia)));

    /// <summary>
    /// Y 2025, que trae la colisión: San Pedro y Sagrado Corazón caen el mismo lunes.
    /// </summary>
    /// <remarks>
    /// Un calendario que contara festivos en vez de mirarlos día a día daría 18 donde hay 17
    /// fechas. Acá no importa —se pregunta por día— y por eso mismo la colisión es gratis; se
    /// prueba para que nadie la «arregle» más adelante.
    /// </remarks>
    [Theory]
    [InlineData(2025, 3, 24)]    // San José: 19-mar es miércoles
    [InlineData(2025, 4, 17)]    // Jueves Santo (Pascua: 20-abr)
    [InlineData(2025, 4, 18)]    // Viernes Santo
    [InlineData(2025, 6, 2)]     // Ascensión
    [InlineData(2025, 6, 23)]    // Corpus
    [InlineData(2025, 6, 30)]    // Sagrado Corazón Y San Pedro, el mismo día
    [InlineData(2025, 11, 17)]   // Cartagena: 11-nov es martes
    public void Los_festivos_de_2025_tambien(int año, int mes, int dia)
        => Assert.False(CalendarioHabil.EsHabil(new DateOnly(año, mes, dia)));

    /// <summary>
    /// Un día de trabajo cualquiera SÍ es hábil.
    /// </summary>
    /// <remarks>
    /// Sin esto, un calendario que dijera «nada es hábil» pasaría todo lo de arriba.
    /// </remarks>
    [Theory]
    [InlineData(2026, 9, 7)]     // lunes normal
    [InlineData(2026, 4, 1)]     // miércoles santo: NO es festivo
    [InlineData(2026, 1, 6)]     // el 6-ene de 2026 se CORRIÓ al 12: ese día se trabaja
    [InlineData(2026, 11, 11)]   // ídem, corrido al 16
    public void Un_dia_de_trabajo_es_habil(int año, int mes, int dia)
        => Assert.True(CalendarioHabil.EsHabil(new DateOnly(año, mes, dia)));

    [Theory]
    [InlineData(2026, 9, 5)]     // sábado
    [InlineData(2026, 9, 6)]     // domingo
    public void El_fin_de_semana_no_es_habil(int año, int mes, int dia)
        => Assert.False(CalendarioHabil.EsHabil(new DateOnly(año, mes, dia)));

    // ── Sumar ───────────────────────────────────────────────────────────────

    /// <summary>
    /// El ejemplo exacto del ticket: 15 hábiles desde el lunes 7-sep-2026.
    /// </summary>
    /// <remarks>
    /// Vence el <b>lunes 28</b>, que es lo que autoró el editor — no el martes 22, que es lo que
    /// contaba el sistema. Seis días de diferencia sin que haya un solo festivo de por medio.
    /// </remarks>
    [Fact]
    public void Quince_habiles_desde_el_lunes_vencen_el_lunes_de_tres_semanas()
        => Assert.Equal(new DateOnly(2026, 9, 28), CalendarioHabil.Sumar(new DateOnly(2026, 9, 7), 15));

    /// <summary>El día de la radicación no cuenta: el término empieza al día siguiente.</summary>
    /// <remarks>
    /// Contándolo, quien radica a las 16:55 gastaría un día entero en cinco minutos.
    /// </remarks>
    [Fact]
    public void El_dia_de_la_radicacion_no_cuenta()
        => Assert.Equal(new DateOnly(2026, 9, 8), CalendarioHabil.Sumar(new DateOnly(2026, 9, 7), 1));

    /// <summary>Un término que cruza Semana Santa se salta el jueves y el viernes.</summary>
    [Fact]
    public void Un_termino_que_cruza_Semana_Santa_salta_los_dos_festivos()
    {
        // Lunes 30-mar + 5 hábiles: 31-mar, 1-abr, (2 y 3 festivos), 6, 7, 8-abr.
        Assert.Equal(new DateOnly(2026, 4, 8), CalendarioHabil.Sumar(new DateOnly(2026, 3, 30), 5));
    }

    /// <summary>Sumar desde un sábado empieza a contar el lunes.</summary>
    [Fact]
    public void Sumar_desde_un_fin_de_semana_arranca_el_lunes()
        => Assert.Equal(new DateOnly(2026, 9, 7), CalendarioHabil.Sumar(new DateOnly(2026, 9, 5), 1));

    /// <summary>
    /// Cero no promete plazo, y negativo tampoco: devuelven el propio día.
    /// </summary>
    /// <remarks>
    /// La ficha lo dice con esas palabras — «en 0 la ficha no promete ningún plazo»— así que
    /// inventar un vencimiento ahí sería contradecir al editor.
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Sin_plazo_no_hay_vencimiento_que_calcular(int dias)
        => Assert.Equal(new DateOnly(2026, 9, 7), CalendarioHabil.Sumar(new DateOnly(2026, 9, 7), dias));

    // ── Entre ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Cero es «hoy es el último día», no «ya se venció».
    /// </summary>
    /// <remarks>
    /// Es la convención que ya tenía el cálculo anterior, y se conserva a propósito: lo que este
    /// arreglo cambia es la UNIDAD, no el punto de corte. Las vistas que distinguen «vence hoy»
    /// de «vencido» siguen diciendo lo mismo.
    /// </remarks>
    [Fact]
    public void El_dia_del_vencimiento_quedan_cero()
        => Assert.Equal(0, CalendarioHabil.Entre(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 28)));

    /// <summary>Lo que queda se cuenta en hábiles, no en calendario.</summary>
    /// <remarks>
    /// Del miércoles 23 al lunes 28 hay cinco días de calendario y <b>tres</b> de trabajo. Con el
    /// vencimiento en hábiles y el resto en calendario, el número diría «quedan 5».
    /// </remarks>
    [Fact]
    public void Lo_que_queda_se_cuenta_en_habiles()
        => Assert.Equal(3, CalendarioHabil.Entre(new DateOnly(2026, 9, 23), new DateOnly(2026, 9, 28)));

    /// <summary>
    /// El atraso también se cuenta en hábiles.
    /// </summary>
    /// <remarks>
    /// Un vencimiento pasado el viernes NO lleva tres días de mora el lunes: lleva uno. Contarlo
    /// en calendario inflaría el retraso de todo lo que cruza un fin de semana.
    /// </remarks>
    [Fact]
    public void El_atraso_tampoco_cuenta_sabados()
        => Assert.Equal(-1, CalendarioHabil.Entre(new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 25)));

    // ── La zona horaria ─────────────────────────────────────────────────────

    /// <summary>
    /// Un radicado a las 23:00 del viernes en Bogotá es viernes, no sábado.
    /// </summary>
    /// <remarks>
    /// Ese instante es sábado en UTC. Contar allá empezaría el término un día tarde, y sólo para
    /// quien radica de noche — un error que aparece y desaparece según la hora.
    /// </remarks>
    [Fact]
    public void El_dia_es_el_de_Colombia_y_no_el_de_UTC()
    {
        var instante = new DateTimeOffset(2026, 9, 26, 4, 0, 0, TimeSpan.Zero);

        Assert.Equal(DayOfWeek.Saturday, instante.UtcDateTime.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 9, 25), CalendarioHabil.EnColombia(instante));
    }
}
