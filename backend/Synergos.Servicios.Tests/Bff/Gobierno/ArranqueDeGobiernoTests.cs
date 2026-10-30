using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Synergos.Bff.Core;
using Synergos.Bff.Gobierno.Domain;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// El orquestador de Gobierno arranca con su flujo declarado (009-gobierno-tramite T022): el host genérico lo valida al
/// arrancar —también contra el catálogo de procesos— y lo sirve en las rutas de la puerta.
/// </summary>
public sealed class ArranqueDeGobiernoTests
{
    [Fact]
    public async Task Arranca_y_sirve_el_flujo_en_las_rutas_de_la_puerta()
    {
        using var r = new RadicacionReal();

        using var salud = await r.Orquestador.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);

        // Sin oferta publicada, cotizar llega hasta precios: el flujo está cargado y no cae a gratis.
        var (estado, cuerpo) = await r.Cotizar();
        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal("pricing.price_not_found", cuerpo.GetProperty("code").GetString());

        var (lista, _) = await r.Listar();
        Assert.Equal(HttpStatusCode.OK, lista);
    }

    [Fact]
    public void La_puerta_ve_de_la_radicacion_sus_dos_fases_cotizar_consultar_cancelar_y_listar()
    {
        using var r = new RadicacionReal();

        var marcadas = r.Servicios.GetRequiredService<EndpointDataSource>().Endpoints
            .SelectMany(e => e.Metadata.OfType<OperacionEnLaPuerta>())
            .Where(o => o.Flujo == "gobierno.radicacion")
            .Select(o => o.Operacion)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["abrir", "cancelar", "cerrar", "consultar", "cotizar", "listar"], marcadas);
    }

    [Fact]
    public void El_abandono_se_barre_a_los_30_minutos_salvo_que_el_despliegue_diga_otra_cosa()
    {
        using var r = new RadicacionReal();
        Assert.Equal(30, r.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);

        using var otro = new RadicacionReal(new Dictionary<string, string> { ["gobierno:Sweep:AbandonAfterMinutes"] = "45" });
        Assert.Equal(45, otro.Servicios.GetRequiredService<IOptions<SweepOptions>>().Value.AbandonAfterMinutes);
    }

    [Fact]
    public void Sin_el_catalogo_de_procesos_no_arranca_y_lo_dice()
    {
        // El flujo arranca un proceso: sin sus definiciones, la transición que el proceso no tiene se descubriría con la
        // primera persona que actúa. El despliegue tiene que poner el fichero (NP-1 de 009).
        var e = Assert.ThrowsAny<Exception>(() =>
        {
            using var r = new RadicacionReal(new Dictionary<string, string> { ["gobierno:Procesos:Archivo"] = "" });
            _ = r.Servicios;
        });
        Assert.Contains("gobierno:Procesos:Archivo", e.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void La_definicion_cotiza_el_tramite_que_publica_la_oferta_y_arranca_la_v2()
    {
        using var definicion = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Proyectos.Dir("Synergos.Bff.Gobierno"), "flujos", "gobierno.radicacion.json")));
        var raiz = definicion.RootElement;
        var pasos = raiz.GetProperty("pasos");

        var tramite = raiz.GetProperty("tipos").GetProperty("tramite");
        Assert.Equal("ref", tramite.GetProperty("tipo").GetString());
        Assert.Equal(OfertaDeGobierno.ClaseDelTramite, tramite.GetProperty("kind").GetString());
        // Sin caer a cero: la AUSENCIA de tasa no radica; la oferta de un trámite gratuito publica su cero.
        Assert.Equal("pricing.cotizar-sin-codigo", pasos.GetProperty("cotizar").GetProperty("tipo").GetString());
        // El expediente nace en la capacidad de procesos con la definición de los nuevos (FR-018), detrás del cobro.
        Assert.Equal("workflow.arrancar", pasos.GetProperty("arrancar").GetProperty("tipo").GetString());
        Assert.Equal("gov.tramite.v2", pasos.GetProperty("arrancar").GetProperty("proceso").GetString());
        Assert.Equal(["capturar", "arrancar", "avisar"], raiz.GetProperty("fases").GetProperty("cerrar").EnumerateArray().Select(e => e.GetString()));
    }
}
