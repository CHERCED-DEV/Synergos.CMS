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
    public void La_puerta_ve_abrir_cerrar_cancelar_consultar_cotizar_y_listar_y_nada_mas()
    {
        using var r = new VisitaReal();

        var marcadas = r.Servicios.GetRequiredService<EndpointDataSource>().Endpoints
            .SelectMany(e => e.Metadata.OfType<OperacionEnLaPuerta>())
            .Where(o => o.Flujo == "propiedades.visita")
            .Select(o => o.Operacion)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["abrir", "cancelar", "cerrar", "consultar", "cotizar", "listar"], marcadas);
    }

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
