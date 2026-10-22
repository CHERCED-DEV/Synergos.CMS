using System.Net;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;

namespace Synergos.CMS.Tests.Bff.Propiedades;

/// <summary>
/// El arranque de <c>Bff.Propiedades</c> (008-propiedades-visita): el host genérico sirve <c>propiedades.visita</c>
/// con sus operaciones marcadas para la puerta, y lo que el despliegue fija mal no deja arrancar.
/// </summary>
public sealed class ArranqueDePropiedadesTests
{
    [Fact]
    public async Task Arranca_y_contesta_health()
    {
        using var r = new VisitaReal();
        using var respuesta = await r.Orquestador.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    public void La_puerta_ve_de_la_visita_sus_fases_cancelar_consultar_cotizar_la_disponibilidad_y_listar_y_nada_mas()
    {
        using var r = new VisitaReal();

        Assert.Equal(["abrir", "cancelar", "cerrar", "consultar", "cotizar", "disponibilidad", "listar"], Marcadas(r, "propiedades.visita"));
    }

    [Fact]
    public void La_puerta_ve_de_la_cancelacion_abrirla_cancelarla_consultarla_y_listarla_y_nada_mas()
    {
        using var r = new VisitaReal();

        // Una fase, y lo posterior no se anticipa (núcleo T085): ni «cerrar» ni «cotizar».
        Assert.Equal(["abrir", "cancelar", "consultar", "listar"], Marcadas(r, "propiedades.visita-cancelacion"));
    }

    private static List<string> Marcadas(VisitaReal r, string flujo)
        => r.Servicios.GetRequiredService<EndpointDataSource>().Endpoints
            .SelectMany(e => e.Metadata.OfType<OperacionEnLaPuerta>())
            .Where(o => o.Flujo == flujo)
            .Select(o => o.Operacion)
            .Order(StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Una_zona_que_no_existe_no_deja_arrancar()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            using var r = new VisitaReal(ajustes: new Dictionary<string, string> { ["Propiedades:ZonaHoraria"] = "Marte/Olimpo" });
        });
    }

    [Fact]
    public void Una_duracion_que_no_es_positiva_no_deja_arrancar()
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            using var r = new VisitaReal(ajustes: new Dictionary<string, string> { ["Propiedades:Visita:Minutos"] = "0" });
        });
    }
}
