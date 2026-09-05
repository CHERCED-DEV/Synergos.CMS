namespace Synergos.CMS.Application.Services;

/// <summary>
/// El calendario de días hábiles de Colombia — sábados, domingos y los festivos de ley.
/// </summary>
/// <remarks>
/// <para><b>Existe porque el schema promete una cosa y el runtime contaba otra</b> (defecto #77).
/// La ficha del trámite dice «cuántos días <i>hábiles</i> tarda la respuesta» y el expediente
/// sumaba días calendario, así que un trámite de 15 días radicado un lunes vencía seis días antes
/// de lo autorado — y eso <b>sin contar un solo festivo</b>. No era sólo un rótulo: el mismo número
/// ordena la cola del funcionario, y como el error crece con la longitud del término, dos trámites
/// con plazos distintos se ordenaban mal <i>entre sí</i>. La cola decía que urgía primero el que no
/// urgía.</para>
///
/// <para><b>Los festivos se CALCULAN, no se listan.</b> Los dieciocho de la Ley 51 de 1983 son
/// deterministas: seis de fecha fija, siete que la regla Emiliani corre al lunes siguiente, y cinco
/// que cuelgan de la Pascua. Una tabla anual sería una cosa más que se vence en silencio —el 1 de
/// enero en que nadie la actualizó, el sistema empieza a contar mal y nada falla—. Por eso no
/// entró «primero sin festivos y luego con ellos»: la mitad fácil habría dejado un plazo <i>casi</i>
/// correcto, que invita a confiar en él.</para>
///
/// <para><b>Vive en el árbol del CMS y no en <c>Synergos.Core</c></b>, que es donde la épica de
/// Gobierno lo imaginaba. No es una discrepancia que resolver: el CMS <b>no puede</b> referenciar
/// aquel árbol —se hablan sólo por HTTP (<c>CLAUDE.md</c> §11)— y hoy el único consumidor es el
/// expediente. El día que una capacidad necesite contar hábiles, ése será el segundo consumidor y
/// entonces se decide dónde vive lo compartido (§17), con el caso delante en vez de en abstracto.
/// Y no es gratis: un calendario <i>colombiano</i> es un sustantivo de negocio, justo lo que §0.B.12
/// prohíbe dentro de una capacidad.</para>
///
/// <para><b>Sin interfaz a propósito.</b> No hay dos implementaciones ni es seam de extensión
/// (<c>CLAUDE.md</c> §6). El día que haga falta otro país, el país será un parámetro o habrá un
/// segundo calendario — y esa decisión se toma entonces.</para>
/// </remarks>
public static class CalendarioHabil
{
    /// <summary>
    /// Colombia no tiene horario de verano: el desfase es fijo.
    /// </summary>
    /// <remarks>
    /// Se cuenta en hora LOCAL y no en UTC porque un radicado a las 23:00 del viernes en Bogotá es
    /// sábado en UTC: contar allá empezaría el término un día tarde, y sólo para quien radica de
    /// noche. Misma convención que <c>UmbracoEventCatalogSource</c>.
    /// </remarks>
    public static readonly TimeSpan DesfaseColombia = TimeSpan.FromHours(-5);

    /// <summary>Si esa fecha es hábil: ni fin de semana ni festivo.</summary>
    public static bool EsHabil(DateOnly dia)
        => dia.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !EsFestivo(dia);

    /// <summary>
    /// Suma <paramref name="habiles"/> días hábiles a partir de <paramref name="desde"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>El día de la radicación no cuenta</b>, y no es una elección de estilo: un término
    /// «de 15 días» empieza a correr al día siguiente. Contando el mismo día, quien radica a las
    /// 16:55 gastaría un día entero en cinco minutos.</para>
    ///
    /// <para>Cero o menos devuelve el propio día: la ficha que no promete plazo no tiene
    /// vencimiento que calcular.</para>
    /// </remarks>
    public static DateOnly Sumar(DateOnly desde, int habiles)
    {
        if (habiles <= 0) return desde;

        var dia = desde;
        for (var contados = 0; contados < habiles;)
        {
            dia = dia.AddDays(1);
            if (EsHabil(dia)) contados++;
        }
        return dia;
    }

    /// <summary>
    /// Cuántos días hábiles quedan de <paramref name="desde"/> a <paramref name="hasta"/>.
    /// </summary>
    /// <remarks>
    /// <para>Negativo si ya se pasó, y ahí cuenta los hábiles de retraso — no los de calendario:
    /// un vencimiento pasado el viernes no lleva tres días de mora el lunes, lleva uno.</para>
    ///
    /// <para><b>Cero significa «hoy es el último día», no «ya se venció»</b>: el término se agota
    /// al terminar el día del vencimiento, no al empezarlo, y a partir de ahí el número es
    /// negativo. Es la misma convención que tenía el cálculo anterior —contaba 0 el día de
    /// vencimiento—, así que lo que corrige esta función es la UNIDAD, no el punto de corte; las
    /// vistas que ya distinguen «vence hoy» de «vencido» siguen diciendo lo mismo.</para>
    /// </remarks>
    public static int Entre(DateOnly desde, DateOnly hasta)
    {
        if (desde == hasta) return 0;

        var adelante = hasta > desde;
        var (a, b) = adelante ? (desde, hasta) : (hasta, desde);

        var habiles = 0;
        for (var dia = a.AddDays(1); dia <= b; dia = dia.AddDays(1))
        {
            if (EsHabil(dia)) habiles++;
        }
        return adelante ? habiles : -habiles;
    }

    /// <summary>La fecha en Colombia del instante dado.</summary>
    public static DateOnly EnColombia(DateTimeOffset instante)
        => DateOnly.FromDateTime(instante.ToOffset(DesfaseColombia).DateTime);

    // ── Los dieciocho ───────────────────────────────────────────────────────

    /// <summary>De fecha fija: la Ley 51 de 1983 NO los corre.</summary>
    private static readonly (int Mes, int Dia)[] Fijos =
    [
        (1, 1),    // Año Nuevo
        (5, 1),    // Día del Trabajo
        (7, 20),   // Independencia
        (8, 7),    // Batalla de Boyacá
        (12, 8),   // Inmaculada Concepción
        (12, 25),  // Navidad
    ];

    /// <summary>De fecha fija que la regla Emiliani corre al lunes siguiente.</summary>
    private static readonly (int Mes, int Dia)[] Trasladables =
    [
        (1, 6),    // Reyes Magos
        (3, 19),   // San José
        (6, 29),   // San Pedro y San Pablo
        (8, 15),   // Asunción
        (10, 12),  // Día de la Raza
        (11, 1),   // Todos los Santos
        (11, 11),  // Independencia de Cartagena
    ];

    /// <summary>
    /// Días desde el Domingo de Pascua. Los dos primeros NO se trasladan.
    /// </summary>
    /// <remarks>
    /// Jueves y Viernes Santo se celebran el día que caen — moverlos rompería la Semana Santa. Los
    /// otros tres son jueves o viernes y Emiliani ya los deja en el lunes siguiente: Ascensión
    /// (+39 → +43), Corpus Christi (+60 → +64) y Sagrado Corazón (+68 → +71).
    /// </remarks>
    private static readonly int[] DesdePascua = [-3, -2, 43, 64, 71];

    private static bool EsFestivo(DateOnly dia)
    {
        foreach (var (mes, d) in Fijos)
        {
            if (dia.Month == mes && dia.Day == d) return true;
        }

        foreach (var (mes, d) in Trasladables)
        {
            if (dia == AlLunesSiguiente(new DateOnly(dia.Year, mes, d))) return true;
        }

        // Ninguno de los siete trasladables cae tan al final del año como para que el lunes
        // siguiente cruce a enero —el último es el 11 de noviembre—, así que construirlos con el
        // año del día que se pregunta es correcto para los siete.
        var pascua = DomingoDePascua(dia.Year);
        foreach (var desfase in DesdePascua)
        {
            if (dia == pascua.AddDays(desfase)) return true;
        }

        return false;
    }

    /// <summary>
    /// La regla Emiliani: si no cae lunes, se corre al lunes siguiente.
    /// </summary>
    private static DateOnly AlLunesSiguiente(DateOnly fecha)
    {
        var faltan = ((int)DayOfWeek.Monday - (int)fecha.DayOfWeek + 7) % 7;
        return fecha.AddDays(faltan);
    }

    /// <summary>
    /// El Domingo de Pascua del año, por el cómputo gregoriano.
    /// </summary>
    /// <remarks>
    /// Cinco de los dieciocho festivos cuelgan de esta fecha, así que sin esto haría falta una
    /// tabla anual escrita a mano — la cosa que se vence en silencio.
    /// </remarks>
    private static DateOnly DomingoDePascua(int año)
    {
        var a = año % 19;
        var b = año / 100;
        var c = año % 100;
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
        return new DateOnly(año, mes, dia);
    }
}
