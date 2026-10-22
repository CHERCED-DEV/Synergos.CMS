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

    /// <summary>El contrato que publica el host de la muestra con sus flujos (también lo usa el gate de la puerta).</summary>
    internal static async Task<JsonObject> Documento()
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

    /// <summary>
    /// N-13 de Tienda: el cuerpo de una fase que continúa (lo efímero de <c>cerrar</c>: la dirección) no es la
    /// entrada del flujo. Con el mismo nombre pisaba al de abrir, y el tipo generado de abrir salía con la dirección.
    /// </summary>
    [Fact]
    public async Task Abrir_y_cotizar_publican_la_entrada_y_una_fase_que_continua_su_propio_cuerpo()
    {
        var doc = await Documento();
        string Ref(string ruta, string? pide = "post")
            => ((string)Op(doc, ruta, pide!)["requestBody"]!["content"]!["application/json"]!["schema"]!["$ref"]!).Split('/')[^1];
        string[] Campos(string esquema) => doc["components"]!["schemas"]![esquema]!["properties"]!.AsObject().Select(p => p.Key).ToArray();

        Assert.Equal("MuestraCompraEntrada", Ref("/v1/flujos/muestra.compra"));
        Assert.Equal("MuestraCompraEntrada", Ref("/v1/flujos/muestra.compra/cotizar"));
        Assert.Equal(["canasta"], Campos("MuestraCompraEntrada"));

        Assert.Equal("MuestraCompraCerrarEntrada", Ref("/v1/flujos/muestra.compra/{id}/cerrar"));
        Assert.Equal(["direccion", "transportadora"], Campos("MuestraCompraCerrarEntrada"));
    }

    [Fact]
    public void Dos_cuerpos_distintos_con_el_mismo_nombre_no_se_publican_y_el_mismo_dos_veces_si()
    {
        var esquemas = new ContratoOpenApi.EsquemasDeFlujos();
        var entrada = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["canasta"] = new JsonObject { ["type"] = "string" } } };

        esquemas.RegistrarCuerpo("PruebaEntrada", entrada);
        esquemas.RegistrarCuerpo("PruebaEntrada", (JsonObject)entrada.DeepClone());

        var otro = new JsonObject { ["type"] = "object", ["properties"] = new JsonObject { ["direccion"] = new JsonObject { ["type"] = "string" } } };
        var ex = Assert.Throws<InvalidOperationException>(() => esquemas.RegistrarCuerpo("PruebaEntrada", otro));
        Assert.Contains("Dos cuerpos distintos se publicarían como «PruebaEntrada»", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_respuesta_tipa_la_salida_que_declara_el_flujo_y_los_genericos_no_se_publican()
    {
        var doc = await Documento();
        var consultar = Op(doc, "/v1/flujos/muestra.cita/{id}", "get");

        Assert.Equal("""{"flujo":"muestra.cita","operacion":"consultar"}""", consultar["x-synergos-flujo"]!.ToJsonString());
        var respuesta = Esquema(doc, consultar["responses"]!["200"]!["content"]!["application/json"]!["schema"]);
        var datos = respuesta["properties"]!["datos"]!["properties"]!.AsObject();
        // Lo que deja para lo posterior (cobroId, lo que reservó) se guarda y NO se publica (D-011-NB10, FR-014 de 011).
        Assert.Equal(new[] { "total", "reservaId" }, datos.Select(p => p.Key));
        Assert.Equal(new[] { "null", "string" }, datos["reservaId"]!["type"]!.AsArray().Select(t => (string)t!).Order(StringComparer.Ordinal));
        Assert.Equal("object", (string?)datos["total"]!["oneOf"]![0]!["type"]);

        var esquemas = doc["components"]!["schemas"]!.AsObject().Select(e => e.Key).ToList();
        Assert.DoesNotContain("RespuestaDeFlujo", esquemas);
        Assert.DoesNotContain("CotizacionDeFlujo", esquemas);
    }

    [Fact]
    public async Task Listar_va_a_la_puerta_con_el_sujeto_y_publica_una_pagina_de_respuestas_tipadas()
    {
        // NB-6: «mis citas». Cada ítem con los datos del flujo, no con el diccionario genérico; el dueño lo pone la
        // puerta, así que el sujeto es requerido y el dueño no es un parámetro de la consulta.
        var doc = await Documento();
        var listar = Op(doc, "/v1/flujos/muestra.cita", "get");

        Assert.Equal("""{"flujo":"muestra.cita","operacion":"listar"}""", listar["x-synergos-flujo"]!.ToJsonString());
        var parametros = listar["parameters"]!.AsArray().ToDictionary(p => (string)p!["name"]!, p => p!);
        Assert.Equal(["X-Synergos-Sujeto", "estado", "limit", "offset"], parametros.Keys.Order(StringComparer.Ordinal));
        Assert.True((bool?)parametros["X-Synergos-Sujeto"]["required"]);

        var pagina = Esquema(doc, listar["responses"]!["200"]!["content"]!["application/json"]!["schema"]);
        Assert.Equal(["hasMore", "items", "offset", "total"], pagina["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        var item = Esquema(doc, pagina["properties"]!["items"]!["items"]);
        Assert.Equal(new[] { "total", "reservaId" }, item["properties"]!["datos"]!["properties"]!.AsObject().Select(p => p.Key));
        Assert.DoesNotContain("PaginaDeFlujoOfRespuestaDeFlujo", doc["components"]!["schemas"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public async Task La_disponibilidad_va_a_la_puerta_sin_sujeto_y_publica_su_entrada_y_su_salida()
    {
        // NB-3: una lectura sin saga. Sin la cabecera del sujeto (la pide un invitado), con su propio cuerpo y su
        // propia respuesta { flujo, datos }; el genérico no sale.
        var doc = await Documento();
        var leer = Op(doc, "/v1/flujos/muestra.cita/disponibilidad", "post");

        Assert.Equal("""{"flujo":"muestra.cita","operacion":"disponibilidad"}""", leer["x-synergos-flujo"]!.ToJsonString());
        Assert.DoesNotContain(leer["parameters"]?.AsArray() ?? [], p => (string?)p!["name"] == CabecerasDeLaPuerta.Sujeto);

        var entrada = Esquema(doc, leer["requestBody"]!["content"]!["application/json"]!["schema"]);
        Assert.Equal(["desde", "hasta", "profesional"], entrada["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        var salida = Esquema(doc, leer["responses"]!["200"]!["content"]!["application/json"]!["schema"]);
        var franjas = salida["properties"]!["datos"]!["properties"]!["franjas"]!;   // anulable, como toda la salida
        Assert.Equal(["array", "null"], franjas["type"]!.AsArray().Select(t => (string)t!).Order(StringComparer.Ordinal));
        Assert.Equal(["fin", "inicio", "libres"], franjas["items"]!["properties"]!.AsObject().Select(p => p.Key).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("LecturaDeFlujo", doc["components"]!["schemas"]!.AsObject().Select(e => e.Key));
    }

    [Fact]
    public async Task Las_operaciones_de_un_flujo_del_personal_salen_marcadas_y_las_del_dueno_no()
    {
        // P8 = A del lote 6: la puerta del CMS pide el rol en lo que el contrato marca «acceso: personal», y la marca de un
        // flujo del dueño no cambia. La fase que abre se llama «devolver», que ya es del vocabulario de la puerta.
        var doc = await Documento();

        Assert.Equal("""{"flujo":"muestra.reembolso","operacion":"devolver","acceso":"personal"}""",
            Op(doc, "/v1/flujos/muestra.reembolso", "post")["x-synergos-flujo"]!.ToJsonString());
        foreach (var (ruta, metodo) in new[]
                 {
                     ("/v1/flujos/muestra.reembolso/{id}", "get"), ("/v1/flujos/muestra.reembolso", "get"),
                     ("/v1/flujos/muestra.reembolso/{id}/cancelar", "post"),
                 })
        {
            Assert.Equal("personal", (string?)Op(doc, ruta, metodo)["x-synergos-flujo"]!["acceso"]);
        }
        Assert.Equal("""{"flujo":"muestra.devolucion","operacion":"devolver"}""",
            Op(doc, "/v1/flujos/muestra.devolucion", "post")["x-synergos-flujo"]!.ToJsonString());
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
