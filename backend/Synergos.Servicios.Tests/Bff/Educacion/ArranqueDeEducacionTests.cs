using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Educacion.Domain;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// El orquestador de Educación arranca con su flujo declarado (010-educacion-matricula T004): el host genérico lo
/// valida al arrancar y lo sirve en las rutas de la puerta.
/// </summary>
public sealed class ArranqueDeEducacionTests
{
    [Fact]
    public async Task Arranca_y_sirve_el_flujo_en_las_rutas_de_la_puerta()
    {
        using var r = new MatriculaDeCursoReal();

        using var salud = await r.Orquestador.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);

        // Sin oferta publicada, cotizar llega hasta precios: el flujo está cargado y su paso propio registrado.
        var (estado, cuerpo) = await r.Cotizar();
        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());

        var (lista, _) = await r.Listar();
        Assert.Equal(HttpStatusCode.OK, lista);
    }

    [Fact]
    public void El_abandono_se_barre_a_los_15_minutos_salvo_que_el_despliegue_diga_otra_cosa()
    {
        using var r = new MatriculaDeCursoReal();
        Assert.Equal(15, r.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);

        using var otro = new MatriculaDeCursoReal(ajustes: new Dictionary<string, string> { ["educacion:Sweep:AbandonAfterMinutes"] = "30" });
        Assert.Equal(30, otro.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);
    }

    [Fact]
    public void La_clase_que_declara_la_definicion_es_la_que_publica_la_oferta_y_deja_el_cobro_para_lo_posterior()
    {
        using var definicion = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Proyectos.Dir("Synergos.Bff.Educacion"), "flujos", "educacion.matricula.json")));
        var raiz = definicion.RootElement;

        var curso = raiz.GetProperty("tipos").GetProperty("curso");
        Assert.Equal("ref", curso.GetProperty("tipo").GetString());
        Assert.Equal(SujetoDelPlan.ClaseDelCurso, curso.GetProperty("kind").GetString());
        // Lote 8, 010-P2: la devolución del personal (fase B) lee el cobro de la original y su total.
        Assert.Equal(["cobroId", "total"], raiz.GetProperty("para_lo_posterior").EnumerateArray().Select(e => e.GetString()));
        // Sin caer a cero: la AUSENCIA de precio rechaza (FR-004), la oferta de un curso gratis publica su cero.
        Assert.Equal("pricing.cotizar-sin-codigo", raiz.GetProperty("pasos").GetProperty("cotizar").GetProperty("tipo").GetString());
    }
}
