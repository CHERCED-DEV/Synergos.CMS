using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// Las rutas de <c>/v1/trips</c>, las que llama el CMS (<c>HttpHotelBookingService</c>, <c>HttpTravelCartEngine</c>), tal como
/// contestan: estado y JSON con las claves que lee <c>ViajesWire.TripDto</c> (línea 006, T004).
/// </summary>
/// <remarks>
/// <b>Lo fija ANTES de tiparlas</b> (molde F2: <c>TypedResults</c>, <c>WithName</c>, la llave declarada): tipar una ruta
/// para publicar su contrato no puede cambiar lo que ve un cliente que ya existe. Con capacidades guionadas, sin red.
/// </remarks>
public sealed class RutasDeHoyDeViajesTests : IDisposable
{
    private const string Llave = "llave-de-prueba";
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "rutas-viajes-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<TripResponse> _fabrica;
    private readonly HttpClient _cliente;

    public RutasDeHoyDeViajesTests()
    {
        var caps = new Guion();
        _fabrica = new WebApplicationFactory<TripResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("viajes:Storage:Root", _raiz);
            b.UseSetting("Viajes:ApiKey", Llave);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IHttpClientFactory>(new Fabrica(caps));
                foreach (var deFondo in s.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType is { } t
                             && (t == typeof(DeliverySweeper) || (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(CompensationSweeper<>))))
                             .ToList())
                {
                    s.Remove(deFondo);
                }
            });
        });
        _cliente = _fabrica.CreateClient();
        _cliente.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
    }

    public void Dispose()
    {
        _cliente.Dispose();
        _fabrica.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    /// <summary>Un guion mínimo: dos productos, el cobro de siempre.</summary>
    private sealed class Guion : HttpMessageHandler
    {
        private int _apartados;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var clave = $"{request.Method.Method} {request.RequestUri!.AbsolutePath}";
            string? json = clave switch
            {
                "POST /v1/quotes" => """{"subtotal":{"amount":500000,"currency":"COP"},"tax":{"amount":0,"currency":"COP"},"total":{"amount":500000,"currency":"COP"}}""",
                "GET /v1/resources" => """{"id":"rec","subjectKind":"viajes.producto","subjectId":"x","capacity":10}""",
                "POST /v1/holds" => $$"""{"id":"h-{{++_apartados}}","resourceId":"rec","expiresAt":"2026-08-05T10:15:00+00:00"}""",
                "POST /v1/payments" => """{"id":"pg1","status":"Authorized","amount":{"amount":500000,"currency":"COP"},"refundable":{"amount":0,"currency":"COP"}}""",
                "POST /v1/payments/pg1/capture" => """{"id":"pg1","status":"Captured","amount":{"amount":500000,"currency":"COP"},"refundable":{"amount":500000,"currency":"COP"}}""",
                "POST /v1/payments/pg1/refund" => """{"id":"pg1","status":"Captured","amount":{"amount":500000,"currency":"COP"},"refundable":{"amount":400000,"currency":"COP"},"refunded":{"amount":100000,"currency":"COP"}}""",
                "GET /v1/payments/pg1" => """{"id":"pg1","status":"Captured","amount":{"amount":500000,"currency":"COP"},"refundable":{"amount":500000,"currency":"COP"}}""",
                _ when clave.EndsWith("/confirm", StringComparison.Ordinal) => """{"id":"res","status":"Confirmed"}""",
                _ => null,
            };
            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"code":"stub.no_route","detail":"sin guion"}""", Encoding.UTF8, "application/problem+json"),
                }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }

    private sealed class Fabrica(Guion guion) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(guion, disposeHandler: false) { BaseAddress = new Uri("http://capacidad.local/") };
    }

    private async Task<(HttpStatusCode Estado, JsonElement Cuerpo, HttpResponseMessage Respuesta)> Enviar(
        HttpMethod metodo, string ruta, object? cuerpo = null, string? llave = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        if (llave is not null) peticion.Headers.Add("Idempotency-Key", llave);
        var r = await _cliente.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone(), r);
    }

    private static readonly object Viaje = new
    {
        travellerKind = "viajes.viajero",
        travellerId = "u-1",
        partialConfirm = true,
        items = new[]
        {
            new { productRef = "vuelo/AV-8020", productLabel = (string?)"Ida", start = "2026-09-01T08:00:00+00:00", end = "2026-09-01T10:00:00+00:00" },
            new { productRef = "hotel/caribe/DBL", productLabel = (string?)null, start = "2026-09-01T15:00:00+00:00", end = "2026-09-03T11:00:00+00:00" },
        },
    };

    /// <summary>Las claves que lee <c>ViajesWire.TripDto</c>, con sus tipos.</summary>
    private static void ComoLoLeeElCms(JsonElement viaje)
    {
        Assert.Equal(JsonValueKind.String, viaje.GetProperty("id").ValueKind);
        Assert.Equal("viajes.viajero", viaje.GetProperty("travellerKind").GetString());
        Assert.Equal("u-1", viaje.GetProperty("travellerId").GetString());
        Assert.Equal(JsonValueKind.String, viaje.GetProperty("status").ValueKind);
        Assert.Equal(500000m, viaje.GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal("COP", viaje.GetProperty("total").GetProperty("currency").GetString());
        Assert.Equal(JsonValueKind.Number, viaje.GetProperty("pendingCompensations").ValueKind);
        Assert.True(viaje.TryGetProperty("lastError", out _));

        var items = viaje.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("vuelo/AV-8020", items[0].GetProperty("productRef").GetString());
        Assert.Equal("Ida", items[0].GetProperty("productLabel").GetString());
        // Sin etiqueta, la etiqueta es el producto (lo que el CMS muestra).
        Assert.Equal("hotel/caribe/DBL", items[1].GetProperty("productLabel").GetString());
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero), items[0].GetProperty("start").GetDateTimeOffset());
        Assert.Equal(JsonValueKind.String, items[0].GetProperty("end").ValueKind);
        Assert.True(items[0].GetProperty("confirmed").ValueKind is JsonValueKind.True or JsonValueKind.False);
        Assert.True(items[0].GetProperty("unfulfilled").ValueKind is JsonValueKind.True or JsonValueKind.False);
    }

    [Fact]
    public async Task Reservar_contesta_201_con_Location_y_el_viaje_que_lee_el_CMS()
    {
        var (estado, cuerpo, r) = await Enviar(HttpMethod.Post, "v1/trips", Viaje, "viaje-1");

        Assert.Equal(HttpStatusCode.Created, estado);
        Assert.Equal("/v1/trips/viaje-1", r.Headers.Location?.OriginalString);
        ComoLoLeeElCms(cuerpo);
        Assert.Equal("Running", cuerpo.GetProperty("status").GetString());
        Assert.False(cuerpo.TryGetProperty("retained", out var ret) && ret.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Reservar_sin_llave_o_sin_viajero_contesta_400_con_su_codigo()
    {
        var (sinLlave, problema, _) = await Enviar(HttpMethod.Post, "v1/trips", Viaje);
        Assert.Equal(HttpStatusCode.BadRequest, sinLlave);
        Assert.StartsWith("viajes.", problema.GetProperty("code").GetString(), StringComparison.Ordinal);

        var (sinViajero, problema2, _) = await Enviar(HttpMethod.Post, "v1/trips", new { items = Array.Empty<object>() }, "viaje-2");
        Assert.Equal(HttpStatusCode.BadRequest, sinViajero);
        Assert.Equal("viajes.bad_traveller", problema2.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Consultar_confirmar_devolver_y_cancelar_contestan_lo_de_siempre()
    {
        await Enviar(HttpMethod.Post, "v1/trips", Viaje, "viaje-1");

        var (consultado, viaje, _) = await Enviar(HttpMethod.Get, "v1/trips/viaje-1");
        Assert.Equal(HttpStatusCode.OK, consultado);
        ComoLoLeeElCms(viaje);

        var (confirmado, viajeConfirmado, _) = await Enviar(HttpMethod.Post, "v1/trips/viaje-1/confirm");
        Assert.Equal(HttpStatusCode.OK, confirmado);
        Assert.Equal("Completed", viajeConfirmado.GetProperty("status").GetString());
        Assert.All(viajeConfirmado.GetProperty("items").EnumerateArray(), i => Assert.True(i.GetProperty("confirmed").GetBoolean()));

        // Devolver una parte EXIGE llave: es un movimiento relativo.
        var (sinLlave, problema, _) = await Enviar(HttpMethod.Post, "v1/trips/viaje-1/refund",
            new { amount = new { amount = 100000m, currency = "COP" }, reason = "auto no cumplido" });
        Assert.Equal(HttpStatusCode.BadRequest, sinLlave);
        Assert.StartsWith("viajes.", problema.GetProperty("code").GetString(), StringComparison.Ordinal);

        var (devuelto, viajeDevuelto, _) = await Enviar(HttpMethod.Post, "v1/trips/viaje-1/refund",
            new { amount = new { amount = 100000m, currency = "COP" }, reason = "auto no cumplido" }, "devolucion-1");
        Assert.Equal(HttpStatusCode.OK, devuelto);
        Assert.Equal(100000m, viajeDevuelto.GetProperty("refunded").GetProperty("amount").GetDecimal());

        var (noExiste, problemaNoExiste, _) = await Enviar(HttpMethod.Post, "v1/trips/otro/cancel");
        Assert.Equal(HttpStatusCode.NotFound, noExiste);
        Assert.Equal("viajes.trip_not_found", problemaNoExiste.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reintentar_y_la_vista_de_lo_colgado_contestan_lo_de_siempre()
    {
        var (noExiste, problema, _) = await Enviar(HttpMethod.Post, "v1/trips/otro/retry");
        Assert.Equal(HttpStatusCode.NotFound, noExiste);
        Assert.Equal("viajes.trip_not_found", problema.GetProperty("code").GetString());

        var (estado, pagina, _) = await Enviar(HttpMethod.Get, "v1/compensations");
        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(JsonValueKind.Array, pagina.GetProperty("items").ValueKind);
        Assert.Equal(0, pagina.GetProperty("total").GetInt32());
        Assert.Equal(0, pagina.GetProperty("offset").GetInt32());
        Assert.False(pagina.GetProperty("hasMore").GetBoolean());
    }
}
