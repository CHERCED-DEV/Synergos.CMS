using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre las reglas que convierten un <c>professionalPage</c> autorado en el profesional que
/// sirve el directorio de Salud (#118).
/// </summary>
/// <remarks>
/// <para>Lo que estas pruebas fijan no es «que parsee»: es <b>qué se niega a inventar</b>. Un
/// profesional del directorio es a quien alguien le pide una cita, así que los tres huecos que
/// podían rellenarse con algo plausible —la semana laboral, la franja de atención y si admite
/// pacientes nuevos— salen vacíos y lo dicen, en vez de salir con un lunes-a-viernes de 8 a 5
/// que nadie escribió.</para>
///
/// <para><b>Y por eso el fixture lleva el caso que el default NO produce.</b> Con todos los
/// profesionales iguales, «admite pacientes» da el mismo resultado se emita o se rellene: la
/// regla sólo se exige cuando hay uno que dice que NO, que es justamente el valor que ningún
/// default fabrica (<c>feedback_an_omitted_key_can_be_an_assertion</c>).</para>
/// </remarks>
public sealed class ProfessionalContentRulesTests
{
    // ── Días de atención ─────────────────────────────────────────────────────

    [Fact]
    public void Los_dias_salen_en_orden_de_semana_y_sin_repetidos()
    {
        // Llegan desordenados y con un repetido a propósito: un desplegable múltiple devuelve
        // los valores en el orden en que el editor los marcó, así que sin ordenar, dos
        // profesionales con los mismos días saldrían con listas distintas.
        var r = ProfessionalContentRules.ParseWorkingDays(
            "ana-rios", ["viernes", "lunes", "miercoles", "lunes"]);

        Assert.Equal(
            new[] { DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday },
            r.Value);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Sin_dias_marcados_NO_se_inventa_una_semana_laboral()
    {
        var r = ProfessionalContentRules.ParseWorkingDays("ana-rios", []);

        Assert.Empty(r.Value);
        Assert.Single(r.Issues);
        Assert.Equal(ProfessionalContentIssueLevel.Warning, r.Issues[0].Level);
    }

    [Fact]
    public void Un_dia_desconocido_se_ignora_y_se_grita_sin_llevarse_los_demas()
    {
        var r = ProfessionalContentRules.ParseWorkingDays("ana-rios", ["lunes", "lunès"]);

        Assert.Equal(new[] { DayOfWeek.Monday }, r.Value);
        Assert.Single(r.Issues);
        Assert.Equal(ProfessionalContentIssueLevel.Error, r.Issues[0].Level);
    }

    // ── Franja de atención ───────────────────────────────────────────────────

    [Fact]
    public void Una_franja_valida_se_sirve_tal_cual()
    {
        var r = ProfessionalContentRules.ParseSlotWindow("ana-rios", 8, 16);

        Assert.Equal((8, 16), r.Value);
        Assert.Empty(r.Issues);
    }

    [Theory]
    [InlineData(16, 8)]   // invertida
    [InlineData(9, 9)]    // sin duración
    [InlineData(8, 24)]   // fuera de rango
    [InlineData(-1, 16)]  // fuera de rango
    public void Una_franja_imposible_se_ANULA_en_vez_de_adivinarse(int inicio, int fin)
    {
        // Servir «8 a 17» porque suena razonable es el respaldo derivado que tapa un hueco con
        // algo plausible: ofrecería horas que nadie dijo que existieran, y una hora ofrecida es
        // alguien presentándose en un consultorio.
        var r = ProfessionalContentRules.ParseSlotWindow("ana-rios", inicio, fin);

        Assert.Equal((0, 0), r.Value);
        Assert.Single(r.Issues);
        Assert.Equal(ProfessionalContentIssueLevel.Error, r.Issues[0].Level);
    }

    [Fact]
    public void Una_franja_sin_escribir_no_es_un_error_pero_tampoco_una_franja()
    {
        var r = ProfessionalContentRules.ParseSlotWindow("ana-rios", 0, 0);

        Assert.Equal((0, 0), r.Value);
        Assert.Single(r.Issues);
        Assert.Equal(ProfessionalContentIssueLevel.Warning, r.Issues[0].Level);
    }

    // ── Duración de la cita ──────────────────────────────────────────────────

    [Fact]
    public void La_duracion_sin_escribir_cae_al_MISMO_default_que_ya_usa_el_cliente_del_BFF()
    {
        // 30 no es un número nuevo: HttpClinicalSchedulingService calcula el fin de la ventana
        // con `SlotMinutes > 0 ? SlotMinutes : 30` desde la HU #25. Otro número aquí dejaría la
        // cita durando una cosa en el CMS y otra contra el orquestador.
        Assert.Equal(30, ProfessionalContentRules.DefaultSlotMinutes);
        Assert.Equal(30, ProfessionalContentRules.ParseSlotMinutes("ana-rios", 0).Value);
        Assert.Equal(45, ProfessionalContentRules.ParseSlotMinutes("ana-rios", 45).Value);
    }

    // ── Admite pacientes nuevos ──────────────────────────────────────────────

    [Fact]
    public void Sin_elegir_es_NO_CONSTA_y_no_un_no()
    {
        var r = ProfessionalContentRules.ParseAcceptingPatients("ana-rios", null);

        Assert.Null(r.Value);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void El_que_dice_que_NO_admite_sale_diciendo_que_no()
    {
        // Es el caso que ningún default produce: `null` es lo que sale solo, `true` es lo que
        // repondría un normalizador defensivo, y `false` sólo puede venir de que alguien lo
        // eligiera. Sin él, emitir el campo o fabricarlo da el mismo resultado.
        var r = ProfessionalContentRules.ParseAcceptingPatients("carlos-mejia", "no");

        Assert.False(r.Value);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void El_que_dice_que_SI_admite_sale_diciendo_que_si()
    {
        Assert.True(ProfessionalContentRules.ParseAcceptingPatients("ana-rios", "si").Value);
    }

    [Fact]
    public void Una_errata_NO_se_lee_como_un_no()
    {
        // Decir que un médico cerró su lista por un dedazo manda a alguien a buscar consulta a
        // otra parte teniendo una disponible.
        var r = ProfessionalContentRules.ParseAcceptingPatients("ana-rios", "nop");

        Assert.Null(r.Value);
        Assert.Single(r.Issues);
        Assert.Equal(ProfessionalContentIssueLevel.Error, r.Issues[0].Level);
    }

    // ── Colisiones de slug ───────────────────────────────────────────────────

    [Fact]
    public void Dos_profesionales_con_el_mismo_slug_se_gritan_porque_son_el_mismo_sujeto()
    {
        // Para Api.Booking el slug ES el subjectId, así que las citas de uno caerían sobre el
        // recurso del otro sin que nada fallara.
        var issues = ProfessionalContentRules.FindSlugCollisions(
            ["ana-rios", "carlos-mejia", "Ana-Rios"]);

        Assert.Single(issues);
        Assert.Equal(ProfessionalContentIssueLevel.Error, issues[0].Level);
        Assert.Contains("ana-rios", issues[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_directorio_sin_repetidos_no_dice_nada()
    {
        Assert.Empty(ProfessionalContentRules.FindSlugCollisions(["ana-rios", "carlos-mejia"]));
    }
}
