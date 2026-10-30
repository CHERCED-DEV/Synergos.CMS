using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.CMS.Tests.Contratos;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La lectura del envío de una compra de la puerta (013-seller-operaciones, T009, R3; interino de NS-2): el CMS la
/// pide para el personal, con la llave compartida, y contesta a dónde va, con quién y por dónde ha ido, de la capacidad
/// de envíos REAL. Sólo de una compra completada y despachada, y sin marca de la puerta: el navegador no la pide.
/// </summary>
public sealed class LecturaDelEnvioTests
{
    private const string Ruta = "/v1/envios/{compra}";

    private static async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Leer(CompraDeTiendaReal t, string compra)
    {
        using var r = await t.Orquestador.GetAsync($"v1/envios/{Uri.EscapeDataString(compra)}");
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    private static async Task<string> Comprada(CompraDeTiendaReal t, string llave = "compra-1")
    {
        var (_, abierta) = await t.Abrir(llave: llave);
        var id = abierta.GetProperty("id").GetString()!;
        var (cerro, cerrada) = await t.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        return id;
    }

    [Fact]
    public async Task De_una_compra_completada_dice_a_donde_va_con_quien_y_por_donde_ha_ido()
    {
        using var t = new CompraDeTiendaReal();
        var compra = await Comprada(t);

        var (estado, cuerpo) = await Leer(t, compra);

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(compra, cuerpo.GetProperty("compra").GetString());
        var envio = cuerpo.GetProperty("envio");
        Assert.Equal(t.Dato(compra, "envio"), envio.GetProperty("id").GetString());
        var destino = envio.GetProperty("destino");
        Assert.Equal(("Calle 1 # 2-3", "Bogotá", "CO"),
            (destino.GetProperty("line1").GetString(), destino.GetProperty("city").GetString(), destino.GetProperty("country").GetString()));
        Assert.Equal("servientrega", envio.GetProperty("transportadora").GetString());
        Assert.Equal("Ready", envio.GetProperty("estado").GetString());
        var evento = Assert.Single(envio.GetProperty("eventos").EnumerateArray());
        Assert.Equal("Ready", evento.GetProperty("estado").GetString());
        Assert.True(evento.TryGetProperty("fecha", out _));
    }

    [Fact]
    public async Task Una_compra_en_curso_una_que_no_existe_y_una_de_otro_flujo_contestan_lo_mismo()
    {
        using var t = new CompraDeTiendaReal();
        var (_, abierta) = await t.Abrir();
        var enCurso = abierta.GetProperty("id").GetString()!;
        var completada = await Comprada(t, "compra-2");
        var (devolvio, devolucion) = await t.Devolver(completada, CompraDeTiendaReal.Pesos(20_000m), "devolucion-1");
        Assert.True(devolvio == HttpStatusCode.Created, devolucion.ToString());

        foreach (var id in new[] { enCurso, "no-existe", devolucion.GetProperty("id").GetString()! })
        {
            var (estado, cuerpo) = await Leer(t, id);
            Assert.Equal(HttpStatusCode.NotFound, estado);
            Assert.Equal("tienda.compra_no_encontrada", cuerpo.GetProperty("code").GetString());
        }
    }

    /// <summary>
    /// En vuelo: «cerrar» ya despachó y está avisando al comprador, así que la saga guarda su <c>envio</c> y todavía no
    /// está completada. Después de despachar sólo quedan pasos que siguen al fallar, así que éste es el caso que
    /// distingue mirar que esté completada de mirar sólo que tenga envío. Completada, se lee.
    /// </summary>
    [Fact]
    public async Task Mientras_cerrar_sigue_en_vuelo_no_hay_envio_que_leer_y_al_completarse_si()
    {
        EnElAviso? compuerta = null;
        using var t = new CompraDeTiendaReal(envolver: f => compuerta = new EnElAviso(f));
        var (_, abierta) = await t.Abrir();
        var compra = abierta.GetProperty("id").GetString()!;

        var cerrar = t.Cerrar(compra);
        await compuerta!.Entro.Task.WaitAsync(Plazo);
        Assert.True(t.Sagas.Find(compra)!.Datos.ContainsKey("envio"), "«cerrar» tenía que haber despachado antes de avisar");
        var (enVuelo, cuerpo) = await Leer(t, compra);
        compuerta.Suelta.SetResult();
        var (cerro, cerrada) = await cerrar;

        Assert.Equal(HttpStatusCode.NotFound, enVuelo);
        Assert.Equal("tienda.compra_no_encontrada", cuerpo.GetProperty("code").GetString());
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal(HttpStatusCode.OK, (await Leer(t, compra)).Estado);
    }

    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(30);

    /// <summary>Detiene el aviso al comprador —«cerrar» ya despachó y cumplió— hasta que el test lo suelta.</summary>
    private sealed class EnElAviso(IHttpClientFactory interno) : IHttpClientFactory
    {
        private int _usada;

        public TaskCompletionSource Entro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Suelta { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public HttpClient CreateClient(string name)
        {
            var dentro = interno.CreateClient(name);
            return new HttpClient(new Paso(this, dentro, name)) { BaseAddress = dentro.BaseAddress, Timeout = Plazo * 2 };
        }

        private sealed class Paso(EnElAviso dueno, HttpClient dentro, string nombre) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage peticion, CancellationToken ct)
            {
                if (nombre == "notifications" && peticion.Method == HttpMethod.Post
                    && peticion.RequestUri!.AbsolutePath.EndsWith("/v1/deliveries", StringComparison.Ordinal)
                    && Interlocked.Exchange(ref dueno._usada, 1) == 0)
                {
                    dueno.Entro.TrySetResult();
                    await dueno.Suelta.Task.WaitAsync(Plazo, ct);
                }

                var copia = new HttpRequestMessage(peticion.Method, peticion.RequestUri) { Content = peticion.Content };
                foreach (var h in peticion.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await dentro.SendAsync(copia, ct);
            }
        }
    }

    [Fact]
    public async Task Con_los_envios_caidos_es_transitorio()
    {
        using var t = new CompraDeTiendaReal(cae: ["GET /v1/shipments/*"]);
        var compra = await Comprada(t);

        var (estado, cuerpo) = await Leer(t, compra);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, estado);
        Assert.True(cuerpo.GetProperty("transient").GetBoolean(), cuerpo.ToString());
    }

    [Fact]
    public void No_lleva_marca_de_la_puerta_ni_en_el_orquestador_ni_en_su_contrato()
    {
        using var t = new CompraDeTiendaReal();

        var endpoint = t.Servicios.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == Ruta);
        Assert.Null(endpoint.Metadata.GetMetadata<OperacionEnLaPuerta>());

        var get = ContratoOpenApi.Comiteado("Synergos.Bff.Tienda")["paths"]![Ruta]!["get"]!.AsObject();
        Assert.Equal("LeerEnvioDeLaCompra", get["operationId"]!.GetValue<string>());
        Assert.False(get.ContainsKey("x-synergos-flujo"), get.ToJsonString());
    }
}
