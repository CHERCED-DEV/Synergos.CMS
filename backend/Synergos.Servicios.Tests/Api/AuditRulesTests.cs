using Synergos.Api.Audit.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="AuditRules"/> — lo que la bitácora rechaza sola.</summary>
public sealed class AuditRulesTests
{
    private static readonly Ref Target = Ref.Create("salud.historia", "h-1");
    private static readonly Actor Alguien = Actor.Of(Ref.Create("identity.member", "m-1"), "editor");

    [Fact]
    public void Una_entrada_ANONIMA_se_rechaza()
    {
        // Una bitácora de acciones anónimas no responde la única pregunta que se le hace.
        var bad = AuditRules.CheckWritable(Actor.Anonymous, "algo.paso", Target, null);

        Assert.Equal("audit.actor_required", bad?.Code);
        Assert.Equal("audit.actor_required", AuditRules.CheckWritable(null, "algo.paso", Target, null)?.Code);
    }

    [Fact]
    public void Un_PROCESO_es_un_actor_valido()
    {
        // Es el corolario del test de arriba: no se prohíbe que actúe una máquina, se prohíbe
        // que no diga cuál. Sin esto, el barrido de retención no podría dejar rastro.
        var cron = Actor.Of(Ref.Create("system", "cron-retencion"));

        Assert.Null(AuditRules.CheckWritable(cron, "retencion.purgo", Target, null));
    }

    [Fact]
    public void Hace_falta_accion_y_objetivo()
    {
        Assert.Equal("audit.bad_action", AuditRules.CheckWritable(Alguien, "  ", Target, null)?.Code);
        Assert.Equal("audit.target_required", AuditRules.CheckWritable(Alguien, "algo.paso", null, null)?.Code);
    }

    [Fact]
    public void Los_detalles_tienen_tope_en_cantidad_y_en_largo()
    {
        // Una bitácora que acepta cualquier volumen termina siendo un vertedero donde nadie
        // puede buscar — y por donde se filtra lo que nadie decidió guardar.
        var muchos = Enumerable.Range(0, AuditRules.MaxDetails + 1).ToDictionary(i => $"k{i}", _ => "v");
        var largo = new Dictionary<string, string> { ["k"] = new('x', AuditRules.MaxDetailLength + 1) };

        Assert.Equal("audit.too_many_details", AuditRules.CheckWritable(Alguien, "a", Target, muchos)?.Code);
        Assert.Equal("audit.detail_too_long", AuditRules.CheckWritable(Alguien, "a", Target, largo)?.Code);
    }

    [Fact]
    public void Una_consulta_SIN_filtro_se_rechaza()
    {
        // Sin filtro es un volcado de la bitácora por HTTP: la forma más cómoda de exfiltrar el
        // rastro entero de una operación.
        Assert.Equal("audit.filter_required", AuditRules.CheckQuery(null, null)?.Code);
        Assert.Null(AuditRules.CheckQuery(Target, null));
        Assert.Null(AuditRules.CheckQuery(null, "m-1"));
    }
}
