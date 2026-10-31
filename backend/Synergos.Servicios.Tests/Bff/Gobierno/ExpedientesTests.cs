using System.Net;
using System.Text.Json;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// La lectura del expediente (009-gobierno-tramite T040): el estado y la historia de la instancia en la capacidad de
/// procesos real, por la radicación que la nombra. La pide sólo el CMS, que es la guarda de quién mira.
/// </summary>
public sealed class ExpedientesTests
{
    private static string Codigo(JsonElement r) => r.GetProperty("code").GetString()!;

    [Fact]
    public async Task Lee_el_estado_y_la_historia_del_proceso_con_quien_actuo_en_cada_acto()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, expediente) = await r.Radicada();
        await r.Tomar(radicacion, funcionario: "f-1", nota: "Lo reviso yo.");
        await r.Decidir(radicacion, "request-info", nota: "Falta el certificado.", funcionario: "f-2");
        await r.Subsanar(radicacion, nota: "Adjunto el certificado.");

        var (estado, leido) = await r.Expediente(radicacion);

        Assert.True(estado == HttpStatusCode.OK, leido.ToString());
        Assert.Equal((radicacion, expediente, "gov.tramite.v2", "in-review"), (
            leido.GetProperty("radicacion").GetString(), leido.GetProperty("expediente").GetString(),
            leido.GetProperty("proceso").GetString(), leido.GetProperty("estado").GetString()));
        var historia = leido.GetProperty("historia").EnumerateArray()
            .Select(h => (h.GetProperty("transicion").GetString(), h.GetProperty("porKind").GetString(), h.GetProperty("porId").GetString(), h.GetProperty("nota").GetString()))
            .ToList();
        Assert.Equal(
            [("take", "gobierno.funcionario", "f-1", "Lo reviso yo."), ("request-info", "gobierno.funcionario", "f-2", "Falta el certificado."),
             ("resubmit", "gobierno.ciudadano", "c-1", "Adjunto el certificado.")],
            historia);
    }

    /// <summary>Lo que no es una radicación —un id que no existe, o la saga de un acto— no tiene expediente.</summary>
    [Fact]
    public async Task Lo_que_no_es_una_radicacion_no_tiene_expediente()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (radicacion, _) = await r.Radicada();
        var (_, toma) = await r.Tomar(radicacion);

        var (ninguna, inexistente) = await r.Expediente("no-existe");
        var (deUnActo, _) = await r.Expediente(toma.GetProperty("id").GetString()!);

        Assert.Equal(HttpStatusCode.NotFound, ninguna);
        Assert.Equal("gobierno.expediente_no_encontrado", Codigo(inexistente));
        Assert.Equal(HttpStatusCode.NotFound, deUnActo);
    }

    [Fact]
    public async Task Una_radicacion_en_curso_todavia_no_tiene_expediente()
    {
        using var r = await new RadicacionReal().ConOferta();
        var (_, abierta) = await r.Abrir();

        var (estado, cuerpo) = await r.Expediente(abierta.GetProperty("id").GetString()!);

        Assert.True(estado == HttpStatusCode.Conflict, cuerpo.ToString());
        Assert.Equal("gobierno.expediente_sin_proceso", Codigo(cuerpo));
    }

    /// <summary>Con la capacidad de procesos caída la lectura lo dice transitorio: el CMS muestra lo último que sabe.</summary>
    [Fact]
    public async Task Con_procesos_caido_lo_dice_y_es_transitorio()
    {
        var caido = false;
        using var r = await new RadicacionReal(falla: p => caido && RadicacionReal.Es(p, "GET", "^/v1/instances/[^/]+$") ? "caida" : null).ConOferta();
        var (radicacion, _) = await r.Radicada();
        caido = true;

        var (estado, cuerpo) = await r.Expediente(radicacion);

        Assert.True((int)estado >= 500, $"{(int)estado} {cuerpo}");
        Assert.True(cuerpo.GetProperty("transient").GetBoolean(), cuerpo.ToString());
    }
}
