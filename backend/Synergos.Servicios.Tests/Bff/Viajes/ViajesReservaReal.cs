using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.Viajes;

/// <summary>
/// La reserva de un viaje DE VERDAD (línea 006, T010): el <c>Program</c> de <c>Bff.Viajes</c>, por las rutas de la puerta,
/// contra la agenda, precios, pagos y avisos reales del arnés.
/// </summary>
/// <remarks>
/// <para><b>Viajes no tiene ruta de ofertas.</b> Cada producto se provisiona como en la copia piloto (D-1 del plan): un
/// recurso <c>viajes.producto</c> en la agenda, SIN horario —un vuelo, una noche de hotel o un día de auto no caben en una
/// grilla—, y una tarifa <c>viajes.tarifa</c> en precios, por el producto entero.</para>
///
/// <para>Lo que ninguna capacidad real sabe hacer a pedido (rechazar una captura) se inyecta en el cable con
/// <c>falla</c>, sin tocar el orquestador.</para>
/// </remarks>
internal sealed class ViajesReservaReal : IDisposable
{
    public const string Llave = "llave-de-viajes";
    public const string Flujo = "v1/flujos/viajes.reserva";

    public const string Vuelo = "vuelo/AV-8020";
    public const string Hotel = "hotel/caribe/DBL";
    public const string Auto = "auto/economico/CTG";

    /// <summary>Dentro de veinte días, a las 10:00 UTC: lejos de cualquier plazo de la agenda.</summary>
    public static readonly DateTimeOffset Salida = new(DateTimeOffset.UtcNow.Date.AddDays(20).AddHours(10), TimeSpan.Zero);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "viajes-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<TripResponse> _fabrica;

    public ViajesReservaReal(Func<HttpRequestMessage, string?>? falla = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");

        IHttpClientFactory fabrica = falla is null ? Capacidades : new ConFallo(Capacidades, falla);
        _fabrica = new WebApplicationFactory<TripResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("viajes:Storage:Root", Path.Combine(_raiz, "viajes"));
            b.UseSetting("Viajes:ApiKey", Llave);
            b.ConfigureTestServices(s => s.AddSingleton(fabrica));
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
    }

    public ArnesDeCapacidades Capacidades { get; }

    public HttpClient Orquestador { get; }

    public SagaEngine<SagaDeFlujo> Sagas => _fabrica.Services.GetRequiredService<SagaEngine<SagaDeFlujo>>();

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal con un fichero aún abierto */ }
    }

    // ── Los productos ───────────────────────────────────────────────────────

    /// <summary>Provisiona un producto: su recurso en la agenda y su tarifa en precios.</summary>
    public async Task<ViajesReservaReal> ConProducto(string producto, decimal precio, int cupos = 10)
    {
        await Crear("booking", "v1/resources", "recurso-" + producto,
            new { subjectKind = "viajes.producto", subjectId = producto, capacity = cupos, timeZoneId = "UTC" });
        await Crear("pricing", "v1/prices", "tarifa-" + producto,
            new { subjectKind = "viajes.tarifa", subjectId = producto, amount = new { amount = precio, currency = "COP" } });
        return this;
    }

    /// <summary>El vuelo (300.000), el hotel (700.000) y el auto (200.000) del itinerario de prueba.</summary>
    public async Task<ViajesReservaReal> ConLosTres()
    {
        await ConProducto(Vuelo, 300_000m);
        await ConProducto(Hotel, 700_000m);
        return await ConProducto(Auto, 200_000m);
    }

    public static object Item(string producto, DateTimeOffset inicio, DateTimeOffset fin, string? etiqueta = null)
        => new { producto, etiqueta, ventana = new { start = inicio, end = fin } };

    /// <summary>Ida en avión, dos noches de hotel y el auto de esos días.</summary>
    public static object[] ItinerarioDePrueba() =>
    [
        Item(Vuelo, Salida, Salida.AddHours(2), "Bogotá → Cartagena"),
        Item(Hotel, Salida.AddHours(5), Salida.AddDays(2).AddHours(1), "Hotel Caribe — doble"),
        Item(Auto, Salida.AddHours(2), Salida.AddDays(2)),
    ];

    // ── Lo que hace quien viaja, por la puerta ─────────────────────────────

    public static string Sujeto(string id) => $"viajes.viajero:{id}";

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "viaje-1", string viajero = "u-1", object[]? items = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, Flujo) { Content = JsonContent.Create(new { items = items ?? ItinerarioDePrueba() }) };
        peticion.Headers.Add("Idempotency-Key", llave);
        return Enviar(peticion, viajero);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string viajero = "u-1")
        => Enviar(new HttpRequestMessage(HttpMethod.Post, $"{Flujo}/{id}/cerrar"), viajero);

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string viajero = "u-1")
        => Enviar(new HttpRequestMessage(HttpMethod.Get, $"{Flujo}/{id}"), viajero);

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string viajero = "u-1")
        => Enviar(new HttpRequestMessage(HttpMethod.Post, $"{Flujo}/{id}/cancelar"), viajero);

    // ── Lo que quedó en las capacidades ─────────────────────────────────────

    public async Task<JsonElement> Leer(string capacidad, string ruta)
    {
        using var http = Capacidades.CreateClient(capacidad);
        return await http.GetFromJsonAsync<JsonElement>(ruta);
    }

    public string Dato(string saga, string nombre) => Sagas.Find(saga)!.Datos[nombre].Valor!.Value.GetString()!;

    /// <summary>Si la saga llegó a guardar un valor con ese nombre (no nulo).</summary>
    public bool Tiene(string saga, string nombre)
        => Sagas.Find(saga) is { } s && s.Datos.TryGetValue(nombre, out var v) && v.Valor is { ValueKind: not JsonValueKind.Null };

    /// <summary>Los apartados que dejó abrir, en el orden de los ítems.</summary>
    public IReadOnlyList<string> Apartados(string saga)
        => (Sagas.Find(saga)!.Reservas.GetValueOrDefault("apartar") ?? []).Select(r => r.HoldId).ToList();

    /// <summary>Las reservas de la agenda a nombre de quien viaja: estado y periodo.</summary>
    public async Task<List<(string Estado, DateTimeOffset Inicio, DateTimeOffset Fin)>> Reservas(string viajero = "u-1")
        => (await Leer("booking", $"v1/reservations?forKind=viajes.viajero&forId={viajero}")).GetProperty("items")
            .EnumerateArray()
            .Select(i => (i.GetProperty("status").GetString()!, i.GetProperty("start").GetDateTimeOffset(), i.GetProperty("end").GetDateTimeOffset()))
            .ToList();

    /// <summary>Cuántos cupos de un producto están tomados en un periodo.</summary>
    public async Task<int> Ocupados(string producto, DateTimeOffset inicio, DateTimeOffset fin)
    {
        var recurso = (await Leer("booking", $"v1/resources/por-sujeto?subjectKind=viajes.producto&subjectId={Uri.EscapeDataString(producto)}"))
            .GetProperty("id").GetString();
        var a = await Leer("booking",
            $"v1/resources/{recurso}/availability?start={Uri.EscapeDataString(inicio.ToString("O"))}&end={Uri.EscapeDataString(fin.ToString("O"))}");
        return a.GetProperty("taken").GetInt32();
    }

    private async Task Crear(string capacidad, string ruta, string llave, object cuerpo)
    {
        using var http = Capacidades.CreateClient(capacidad);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion, string viajero)
    {
        using (peticion)
        {
            peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(viajero));
            using var r = await Orquestador.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
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
}
