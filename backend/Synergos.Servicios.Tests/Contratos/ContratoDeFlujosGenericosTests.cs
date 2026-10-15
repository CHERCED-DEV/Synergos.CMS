using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Architecture;
using Synergos.CMS.Tests.Bff.FlujoDeMuestra;

namespace Synergos.CMS.Tests.Contratos;

/// <summary>
/// Lo que el host publica de un flujo genérico (núcleo N1): sus operaciones por convención con la marca de
/// la puerta, y su cuerpo y su salida TIPADOS desde la definición, que es lo que el generador del UI necesita
/// para no escribir a mano el envoltorio del flujo.
/// </summary>
/// <remarks>
/// Se genera del host de la muestra (<see cref="ArranqueDeLaMuestra"/>) con las mismas opciones que los
/// contratos comiteados (<see cref="ContratoOpenApi.Opciones"/>), sin red: generar el documento no llama a
/// ninguna capacidad.
/// </remarks>
public sealed class ContratoDeFlujosGenericosTests
{
    private sealed class SinRed : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Rechaza());

        private sealed class Rechaza : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => throw new InvalidOperationException("Generar el contrato no llama a ninguna capacidad.");
        }
    }

    private static async Task<JsonObject> Documento()
    {
        var raiz = Path.Combine(Path.GetTempPath(), "contrato-muestra-" + Guid.NewGuid().ToString("N"));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["muestra:Flujos:Carpeta"] = Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "FlujoDeMuestra", "flujos"),
            ["muestra:Storage:Root"] = raiz,
            ["Muestra:ApiKey"] = "llave",
        });
        builder.Services.AddSingleton<IHttpClientFactory>(new SinRed());
        builder.Services.AddOpenApi(ContratoOpenApi.Documento, ContratoOpenApi.Opciones("Synergos.Bff.Muestra"));

        await using var app = ArranqueDeLaMuestra.Construir(builder);
        await app.StartAsync();
        try
        {
            var proveedor = app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(ContratoOpenApi.Documento);
            return JsonNode.Parse(await ContratoOpenApi.Serializar(await proveedor.GetOpenApiDocumentAsync(CancellationToken.None)))!.AsObject();
        }
        finally
        {
            await app.StopAsync();
            try { if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }
    }

    private static JsonObject Op(JsonObject doc, string ruta, string metodo) => doc["paths"]![ruta]![metodo]!.AsObject();

    private static JsonObject Esquema(JsonObject doc, JsonNode? referencia)
        => doc["components"]!["schemas"]![((string)referencia!["$ref"]!).Split('/')[^1]]!.AsObject();

    [Fact]
    public async Task Abrir_publica_su_cuerpo_tipado_su_llave_de_saga_y_el_sujeto_que_pone_la_puerta()
    {
        var doc = await Documento();
        var abrir = Op(doc, "/v1/flujos/muestra.cita", "post");

        Assert.Equal("MuestraCitaAbrir", (string?)abrir["operationId"]);
        Assert.Equal("""{"flujo":"muestra.cita","operacion":"abrir"}""", abrir["x-synergos-flujo"]!.ToJsonString());

        var cuerpo = Esquema(doc, abrir["requestBody"]!["content"]!["application/json"]!["schema"]);
        Assert.Equal(new[] { "profesional", "ventana", "servicio", "promocion" }, cuerpo["properties"]!.AsObject().Select(p => p.Key));
        Assert.Equal("""["profesional","ventana","servicio"]""", cuerpo["required"]!.ToJsonString());
        // La clase que declara la definición (NR-1 de 008) es la única que el contrato admite.
        Assert.Equal("""["muestra.profesional"]""", cuerpo["properties"]!["profesional"]!["properties"]!["kind"]!["enum"]!.ToJsonString());
        Assert.Equal("""["muestra.servicio"]""", cuerpo["properties"]!["servicio"]!["properties"]!["kind"]!["enum"]!.ToJsonString());

        var parametros = abrir["parameters"]!.AsArray().Select(p => p!.AsObject()).ToList();
        var llave = parametros.Single(p => (string?)p["name"] == "Idempotency-Key");
        Assert.True((bool)llave["required"]!);
        Assert.Equal(LlaveDeSaga.MaxLength, (int)llave["schema"]!["maxLength"]!);
        var sujeto = parametros.Single(p => (string?)p["name"] == CabecerasDeLaPuerta.Sujeto);
        Assert.True((bool)sujeto["required"]! && (bool)sujeto["x-synergos-puerta"]!);
    }

    [Fact]
    public async Task La_respuesta_tipa_la_salida_que_declara_el_flujo_y_los_genericos_no_se_publican()
    {
        var doc = await Documento();
        var consultar = Op(doc, "/v1/flujos/muestra.cita/{id}", "get");

        Assert.Equal("""{"flujo":"muestra.cita","operacion":"consultar"}""", consultar["x-synergos-flujo"]!.ToJsonString());
        var respuesta = Esquema(doc, consultar["responses"]!["200"]!["content"]!["application/json"]!["schema"]);
        var datos = respuesta["properties"]!["datos"]!["properties"]!.AsObject();
        Assert.Equal(new[] { "total", "reservaId" }, datos.Select(p => p.Key));
        Assert.Equal(new[] { "null", "string" }, datos["reservaId"]!["type"]!.AsArray().Select(t => (string)t!).Order(StringComparer.Ordinal));
        Assert.Equal("object", (string?)datos["total"]!["oneOf"]![0]!["type"]);

        var esquemas = doc["components"]!["schemas"]!.AsObject().Select(e => e.Key).ToList();
        Assert.DoesNotContain("RespuestaDeFlujo", esquemas);
        Assert.DoesNotContain("CotizacionDeFlujo", esquemas);
    }

    [Fact]
    public async Task Cerrar_cancelar_y_cotizar_van_a_la_puerta_y_lo_de_operacion_no()
    {
        var doc = await Documento();

        Assert.Equal("cerrar", (string?)Op(doc, "/v1/flujos/muestra.cita/{id}/cerrar", "post")["x-synergos-flujo"]!["operacion"]);
        Assert.Equal("cancelar", (string?)Op(doc, "/v1/flujos/muestra.cita/{id}/cancelar", "post")["x-synergos-flujo"]!["operacion"]);
        Assert.Equal("cotizar", (string?)Op(doc, "/v1/flujos/muestra.cita/cotizar", "post")["x-synergos-flujo"]!["operacion"]);

        var contacto = Op(doc, "/v1/flujos/muestra.cita/{id}/cerrar", "post")["parameters"]!.AsArray()
            .Single(p => (string?)p!["name"] == CabecerasDeLaPuerta.Contacto)!;
        Assert.NotEqual(true, (bool?)contacto["required"]);

        Assert.Null(Op(doc, "/v1/flujos/muestra.cita/{id}/reintentar", "post")["x-synergos-flujo"]);
        Assert.Null(Op(doc, "/v1/flujos/compensaciones", "get")["x-synergos-flujo"]);
    }
}
