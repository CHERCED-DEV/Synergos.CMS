using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.FlujoDeMuestra;

/// <summary>
/// Una compra del caso común —leer la canasta, cotizar cada línea, registrar el pedido y autorizar; capturar,
/// despachar, cumplir el pedido y cerrar la canasta— declarada en JSON y servida por el host genérico contra
/// las capacidades REALES en proceso (núcleo N2, historia 4).
/// </summary>
/// <remarks>
/// Lo mismo que la cita (<see cref="FlujoDeMuestraTests"/>) con las cuatro piezas de N2: Cart, Orders y
/// Fulfillment, más el precio de cada línea. Lo que ninguna real sabe hacer a pedido (rechazar un cobro, no
/// dejar cumplir un pedido) se inyecta en el cable, sin tocar el host.
/// </remarks>
public sealed class CompraDeMuestraTests
{
    private const string Llave = "llave-de-la-muestra";
    private const string Comprador = "muestra.comprador:u-1";

    private static string Carpeta() => Path.Combine(Proyectos.Dir("Synergos.Servicios.Tests"), "Bff", "FlujoDeMuestra", "flujos");

    private sealed class Tienda : IAsyncDisposable
    {
        private readonly string _raiz = Path.Combine(Path.GetTempPath(), "muestra-compra-" + Guid.NewGuid().ToString("N"));
        private WebApplication? _app;

        public ArnesDeCapacidades Capacidades { get; } = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Cart.Contracts.CartResponse>("cart")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Orders.Contracts.OrderResponse>("orders")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Fulfillment.Contracts.ShipmentResponse>("fulfillment");

        public HttpClient Cliente { get; private set; } = null!;
        public string Canasta { get; private set; } = string.Empty;

        public SagaEngine<SagaDeFlujo> Sagas => _app!.Services.GetRequiredService<SagaEngine<SagaDeFlujo>>();

        public static async Task<Tienda> Arrancar(Func<HttpRequestMessage, string?>? falla = null)
        {
            var t = new Tienda();
            await t.Aprovisionar();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
            builder.WebHost.UseTestServer();
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["muestra:Flujos:Carpeta"] = Carpeta(),
                ["muestra:Storage:Root"] = t._raiz,
                ["Muestra:ApiKey"] = Llave,
            });
            builder.Services.AddSingleton<IHttpClientFactory>(falla is null ? t.Capacidades : new ConFallo(t.Capacidades, falla));

            t._app = ArranqueDeLaMuestra.Construir(builder);
            await t._app.StartAsync();
            t.Cliente = t._app.GetTestClient();
            t.Cliente.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
            return t;
        }

        public async ValueTask DisposeAsync()
        {
            Cliente.Dispose();
            if (_app is not null) await _app.DisposeAsync();
            Capacidades.Dispose();
            try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
            catch (IOException) { /* un temporal */ }
        }

        private async Task Aprovisionar()
        {
            await Post("pricing", "v1/prices", new
            {
                subjectKind = "muestra.producto", subjectId = "taza", amount = new { amount = 20_000m, currency = "COP" }, taxRateBasisPoints = 0,
            }, "precio");
            var canasta = await Post("cart", "v1/carts", new { ownerKind = "muestra.comprador", ownerId = "u-1", assertion = "CmsSession" }, "canasta");
            Canasta = canasta.GetProperty("id").GetString()!;
            using var http = Capacidades.CreateClient("cart");
            using var r = await http.PostAsJsonAsync($"v1/carts/{Canasta}/lines", new { subjectKind = "muestra.producto", subjectId = "taza", quantity = 2 });
            Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        }

        private async Task<JsonElement> Post(string capacidad, string ruta, object cuerpo, string llave)
        {
            using var http = Capacidades.CreateClient(capacidad);
            using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
            peticion.Headers.Add("Idempotency-Key", llave);
            using var r = await http.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {texto}");
            return JsonDocument.Parse(texto).RootElement.Clone();
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir()
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/muestra.compra") { Content = JsonContent.Create(new { canasta = Canasta }) };
            peticion.Headers.Add("Idempotency-Key", "compra-1");
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Comprador);
            return Enviar(peticion);
        }

        public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id)
        {
            var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/muestra.compra/{id}/cerrar")
            {
                Content = JsonContent.Create(new
                {
                    direccion = new { linea1 = "Calle 1 # 2-3", ciudad = "Bogotá", pais = "CO", contacto = "Ana" },
                    transportadora = "servientrega",
                }),
            };
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Comprador);
            return Enviar(peticion);
        }

        public async Task<JsonElement> Leer(string capacidad, string ruta)
        {
            using var http = Capacidades.CreateClient(capacidad);
            return await http.GetFromJsonAsync<JsonElement>(ruta);
        }

        public string Dato(string id, string nombre) => Sagas.Find(id)!.Datos[nombre].Valor!.Value.GetString()!;

        private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
        {
            using (peticion)
            {
                using var r = await Cliente.SendAsync(peticion);
                var texto = await r.Content.ReadAsStringAsync();
                return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
            }
        }
    }

    /// <summary>Contesta un rechazo en el cable cuando <c>falla</c> devuelve un código; si no, deja pasar.</summary>
    private sealed class ConFallo(IHttpClientFactory real, Func<HttpRequestMessage, string?> falla) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var interno = real.CreateClient(name);
            return new HttpClient(new Cable(interno, falla)) { BaseAddress = interno.BaseAddress };
        }

        private sealed class Cable(HttpClient interno, Func<HttpRequestMessage, string?> falla) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                if (falla(r) is { } codigo)
                {
                    return new HttpResponseMessage(HttpStatusCode.Conflict)
                    {
                        Content = JsonContent.Create(new { code = codigo, detail = "inyectado en el cable" }),
                    };
                }

                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await interno.SendAsync(copia, ct);
            }
        }
    }

    private static bool Es(HttpRequestMessage r, string metodo, string patron)
        => r.Method.Method == metodo && Regex.IsMatch(r.RequestUri!.AbsolutePath, patron);

    [Fact]
    public async Task Compra_la_canasta_con_el_precio_de_cada_linea_despacha_cumple_y_la_cierra()
    {
        await using var t = await Tienda.Arrancar();

        var (estado, abierta) = await t.Abrir();
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal(40_000m, abierta.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        var id = abierta.GetProperty("id").GetString()!;

        var (cerro, cerrada) = await t.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());

        var pedido = await t.Leer("orders", $"v1/orders/{t.Dato(id, "pedido")}");
        Assert.Equal("Fulfilled", pedido.GetProperty("status").GetString());
        Assert.Equal(20_000m, pedido.GetProperty("lines")[0].GetProperty("unitPrice").GetProperty("amount").GetDecimal());
        var envio = await t.Leer("fulfillment", $"v1/shipments/{t.Dato(id, "envio")}");
        Assert.Equal("orders.pedido", envio.GetProperty("forKind").GetString());
        Assert.True((await t.Leer("cart", $"v1/carts/{t.Canasta}")).GetProperty("checkedOut").GetBoolean());
        Assert.Equal("Captured", (await t.Leer("payments", $"v1/payments/{t.Dato(id, "cobroId")}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Un_cobro_rechazado_cancela_el_pedido_y_devuelve_el_rechazo_original()
    {
        await using var t = await Tienda.Arrancar(falla: r => Es(r, "POST", "^/v1/payments$") ? "payments.payment_declined" : null);

        var (estado, cuerpo) = await t.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, t.Sagas.Find("compra-1")!.Status);
        Assert.Equal("Cancelled", (await t.Leer("orders", $"v1/orders/{t.Dato("compra-1", "pedido")}")).GetProperty("status").GetString());
        Assert.False((await t.Leer("cart", $"v1/carts/{t.Canasta}")).GetProperty("checkedOut").GetBoolean());
    }

    [Fact]
    public async Task El_doble_clic_no_despacha_dos_veces()
    {
        await using var t = await Tienda.Arrancar();
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK, (await t.Cerrar(id)).Estado);
        var (otraVez, cuerpo) = await t.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, otraVez);
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        var envios = await t.Leer("fulfillment", $"v1/shipments?forKind=orders.pedido&forId={t.Dato(id, "pedido")}");
        Assert.Equal(1, envios.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Si_el_pedido_no_se_deja_cumplir_tras_despachar_la_compra_se_completa_y_el_pedido_se_concilia()
    {
        // N-3 de Tienda: con la plata cobrada y el envío hecho, un pedido sin cumplir se concilia, no se anula.
        await using var t = await Tienda.Arrancar(falla: r => Es(r, "POST", "^/v1/orders/[^/]+/fulfill$") ? "orders.order_closed" : null);
        var (_, abierta) = await t.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        var (estado, cerrada) = await t.Cerrar(id);

        Assert.True(estado == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Placed", (await t.Leer("orders", $"v1/orders/{t.Dato(id, "pedido")}")).GetProperty("status").GetString());
        Assert.Equal("Captured", (await t.Leer("payments", $"v1/payments/{t.Dato(id, "cobroId")}")).GetProperty("status").GetString());
    }
}
