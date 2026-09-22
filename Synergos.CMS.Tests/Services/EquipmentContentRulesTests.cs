using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las reglas del contenido de un equipo (#147, S2).
/// </summary>
/// <remarks>
/// Los fixtures llevan el caso que la regla RESUELVE y no el bonito: un tramo en día 0, dos
/// tramos en el mismo día, un máximo por encima del tope. Con tramos bien puestos, tener la
/// regla o no da el mismo resultado y el defecto pasaría en verde.
/// </remarks>
public sealed class EquipmentContentRulesTests
{
    private static EquipmentRate Tramo(string code, int minDays, decimal perDay)
        => new(code, code, minDays, perDay, string.Empty);

    [Fact]
    public void Un_maximo_por_encima_del_tope_del_despliegue_se_acota()
    {
        var r = EquipmentContentRules.ParseDayBounds("andamio", rawMin: 1, rawMax: 90, deploymentMaxDays: 30);

        // Una autorización de garantía no dura 90 días: por eso el tope del despliegue manda.
        Assert.Equal(30, r.Value.Max);
        Assert.Equal(EquipmentContentIssueLevel.Warning, Assert.Single(r.Issues).Level);
    }

    [Fact]
    public void Vacio_es_un_dia_de_minimo_y_el_tope_de_maximo()
    {
        var r = EquipmentContentRules.ParseDayBounds("andamio", rawMin: 0, rawMax: 0, deploymentMaxDays: 30);

        Assert.Equal(1, r.Value.Min);
        Assert.Equal(30, r.Value.Max);
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Un_minimo_mayor_que_el_maximo_se_acota_al_maximo()
    {
        var r = EquipmentContentRules.ParseDayBounds("andamio", rawMin: 15, rawMax: 7, deploymentMaxDays: 30);

        Assert.Equal(7, r.Value.Min);
        Assert.Equal(7, r.Value.Max);
        Assert.Single(r.Issues);
    }

    [Fact]
    public void Un_tramo_que_empieza_en_cero_dias_se_DESCARTA_y_no_se_sube_a_uno()
    {
        // Subirlo a 1 lo pondría a competir con la tarifa base por el mismo día, y cuál gana
        // dependería del orden en que el editor los arrastró: el desempate de #131.
        var r = EquipmentContentRules.ParseRates("andamio", new[] { Tramo("malo", 0, 1000m), Tramo("semana", 7, 800m) });

        Assert.Equal(new[] { "semana" }, r.Value.Select(x => x.Code));
        Assert.Single(r.Issues);
    }

    [Fact]
    public void Dos_tramos_en_el_mismo_dia_se_descartan_LOS_DOS()
    {
        // Servir uno dejaría que republicar cambiara el precio sin que nadie lo decidiera.
        var r = EquipmentContentRules.ParseRates("andamio",
            new[] { Tramo("a", 7, 800m), Tramo("b", 7, 600m), Tramo("mes", 30, 500m) });

        Assert.Equal(new[] { "mes" }, r.Value.Select(x => x.Code));
        Assert.Contains("se descartan todos", Assert.Single(r.Issues).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_tramos_salen_ordenados_por_dia_aunque_el_editor_los_arrastrara_al_reves()
    {
        var r = EquipmentContentRules.ParseRates("andamio",
            new[] { Tramo("mes", 30, 500m), Tramo("semana", 7, 800m), Tramo("dia", 1, 1000m) });

        Assert.Equal(new[] { 1, 7, 30 }, r.Value.Select(x => x.MinDays));
        Assert.Empty(r.Issues);
    }

    [Fact]
    public void Un_tramo_con_valor_negativo_se_descarta()
    {
        var r = EquipmentContentRules.ParseRates("andamio", new[] { Tramo("raro", 3, -100m) });

        Assert.Empty(r.Value);
        Assert.Single(r.Issues);
    }

    [Fact]
    public void Sin_tramos_no_hay_reparos()
    {
        Assert.Empty(EquipmentContentRules.ParseRates("andamio", null).Value);
        Assert.Empty(EquipmentContentRules.ParseRates("andamio", Array.Empty<EquipmentRate>()).Issues);
    }

    [Fact]
    public void Dos_equipos_con_el_mismo_slug_es_un_ERROR_y_no_un_aviso()
    {
        var issues = EquipmentContentRules.FindSlugCollisions(new[] { "andamio", "taladro", "andamio" });

        Assert.Single(issues);
        Assert.Equal(EquipmentContentIssueLevel.Error, issues[0].Level);
        Assert.Contains("andamio", issues[0].Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Slugs_que_solo_difieren_en_mayusculas_son_equipos_DISTINTOS()
    {
        // Ordinal a propósito: el slug viaja como subjectId hasta Api.Booking, que no normaliza.
        Assert.Empty(EquipmentContentRules.FindSlugCollisions(new[] { "andamio", "Andamio" }));
    }
}
