using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Reservas.Contracts;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Reservas;

/// <summary>
/// La reserva de un servicio DE VERDAD (011-booking-reserva): el <c>Program</c> de <c>Bff.Reservas</c> contra la
/// agenda, precios, pagos y avisos reales del arnés, con lo que pone la puerta en cabeceras.
/// </summary>
/// <remarks>
/// <para>El servicio se publica por la RUTA DE OFERTAS del propio orquestador, como lo hará el CMS y el guion de la
/// copia piloto: así la reserva sólo encuentra lo que la oferta dejó (recurso, tarifa y su duración).</para>
///
/// <para>Lo que ninguna capacidad real sabe hacer a pedido (rechazar un cobro, fallar una confirmación) se inyecta
/// en el cable con <c>falla</c>, sin tocar el orquestador.</para>
/// </remarks>
internal sealed class ReservaDeServicioReal : IDisposable
{
    public const string Llave = "llave-de-reservas";
    public const string Servicio = "masaje-60";

    /// <summary>La categoría con que el arnés registra cada llamada a una capacidad.</summary>
    public const string LlamadasAlArnes = "Synergos.Arnes.Llamadas";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "reservas-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<OfertaDeServicioResponse> _fabrica;

    /// <summary>Un día hábil a diez días, a las 10:00 UTC: dentro del horario de la oferta de prueba.</summary>
    public static readonly DateTimeOffset Inicio = new(DateTimeOffset.UtcNow.Date.AddDays(10).AddHours(10), TimeSpan.Zero);

    public ReservaDeServicioReal(
        Func<HttpRequestMessage, string?>? falla = null, IReadOnlyDictionary<string, string>? ajustes = null,
        ILoggerProvider? registro = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");
        PublicarPlantilla().GetAwaiter().GetResult();

        IHttpClientFactory fabrica = falla is null ? Capacidades : new ConFallo(Capacidades, falla);
        // Con registro, cada llamada a una capacidad deja su línea, como el manejador de registro del cliente en un
        // despliegue: el arnés sustituye la fábrica y sin esto ninguna llamada se vería en el registro.
        if (registro is not null) fabrica = new ConRegistro(fabrica, registro.CreateLogger(LlamadasAlArnes));
        _fabrica = new WebApplicationFactory<OfertaDeServicioResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("reservas:Storage:Root", Almacen);
            b.UseSetting("Reservas:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(fabrica);
                if (registro is not null) s.AddLogging(l => l.AddProvider(registro).SetMinimumLevel(LogLevel.Trace));
            });
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
    }

    public ArnesDeCapacidades Capacidades { get; }

    public HttpClient Orquestador { get; }

    public IServiceProvider Servicios => _fabrica.Services;

    public SagaEngine<SagaDeFlujo> Sagas => Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>();

    /// <summary>La raíz del almacén del orquestador (sus sagas y el rastro de sus ofertas).</summary>
    public string Almacen => Path.Combine(_raiz, "reservas");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal con un fichero aún abierto */ }
    }

    // ── La oferta ───────────────────────────────────────────────────────────

    /// <summary>La oferta de prueba: 60 minutos, todos los días de 08:00 a 20:00 UTC, dos cupos.</summary>
    public static object Oferta(string servicio = Servicio, decimal precio = 140_000m, int duracion = 60, int cupos = 2,
        string desde = "08:00", string hasta = "20:00", int plazoHoras = 24)
        => new
        {
            servicio,
            nombre = "Masaje terapéutico · 60 min",
            duracionMinutos = duracion,
            horario = Enum.GetValues<DayOfWeek>().Select(d => new { dia = d.ToString(), desde, hasta }).ToList(),
            cupos,
            plazoCancelacionHoras = plazoHoras,
            precio,
            moneda = "COP",
            zona = "UTC",
        };

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(object? oferta = null, string llave = "oferta-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta ?? Oferta()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string servicio = Servicio, string llave = "retiro-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{servicio}/retirar");
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    /// <summary>Publica la oferta de prueba y falla si no queda publicada.</summary>
    public async Task<ReservaDeServicioReal> ConOferta(object? oferta = null)
    {
        var (estado, cuerpo) = await Publicar(oferta);
        Assert.True(estado == HttpStatusCode.OK, $"La oferta no se publicó: {(int)estado} {cuerpo}");
        return this;
    }

    // ── Lo que hace quien reserva, por la puerta ───────────────────────────

    public static string Sujeto(string id) => $"reservas.cliente:{id}";

    /// <summary>El cuerpo de abrir o cotizar: el servicio es una referencia con la clase que declara la definición (NR-1).</summary>
    public static object Cuerpo(string servicio = Servicio, DateTimeOffset? inicio = null, int minutos = 60, string clase = "reservas.servicio")
    {
        var desde = inicio ?? Inicio;
        return new { servicio = new { kind = clase, id = servicio }, ventana = new { start = desde, end = desde.AddMinutes(minutos) } };
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "reserva-1", string cliente = "u-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/reservas.servicio") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(cliente));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(string cliente = "u-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/reservas.servicio/cotizar") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(cliente));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string cliente = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/reservas.servicio/{id}/cerrar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(cliente));
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { correo = "ana@ejemplo.co", nombre = "Ana", enlace = "https://sitio.ejemplo/booking/reservar/?reserva=x", sitio = "Spa de prueba" }))));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string cliente = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/reservas.servicio/{id}");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(cliente));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string cliente = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/reservas.servicio/{id}/cancelar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(cliente));
        return Enviar(peticion);
    }

    /// <summary>La lectura de disponibilidad, SIN sujeto: la pide quien todavía no inició sesión (NB-3).</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Disponibilidad(
        DateTimeOffset desde, DateTimeOffset hasta, string servicio = Servicio, string clase = "reservas.servicio")
        => Enviar(new HttpRequestMessage(HttpMethod.Post, "v1/flujos/reservas.servicio/disponibilidad")
        {
            Content = JsonContent.Create(new { servicio = new { kind = clase, id = servicio }, desde, hasta }),
        });

    /// <summary>«Mis reservas» (NB-6): las del sujeto que pone la puerta, con la consulta tal cual (<c>?estado=…</c>).</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Listar(string consulta = "", string cliente = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, "v1/flujos/reservas.servicio" + consulta);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(cliente));
        return Enviar(peticion);
    }

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

    /// <summary>Cuántos avisos salieron para el cliente.</summary>
    public async Task<int> Avisos(string cliente = "u-1")
        => (await Leer("notifications", $"v1/deliveries?toKind=reservas.cliente&toId={cliente}")).GetProperty("items").GetArrayLength();

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
    {
        using (peticion)
        {
            using var r = await Orquestador.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
        }
    }

    /// <summary>La plantilla del aviso, del fichero que la provisiona de verdad.</summary>
    private async Task PublicarPlantilla()
    {
        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = fichero.RootElement.EnumerateArray()
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "reservas.servicio.confirmada");
        using var http = Capacidades.CreateClient("notifications");
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/templates")
        {
            Content = JsonContent.Create(new
            {
                key = p.GetProperty("key").GetString(),
                channel = p.GetProperty("channel").GetString(),
                subject = p.GetProperty("subject").GetString(),
                body = p.GetProperty("body").GetString(),
            }),
        };
        peticion.Headers.Add("Idempotency-Key", "plantilla-reservas");
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"plantilla: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    public static bool Es(HttpRequestMessage r, string metodo, string patron)
        => r.Method.Method == metodo && Regex.IsMatch(r.RequestUri!.AbsolutePath, patron);

    /// <summary>Escribe una línea por llamada (<c>Llamada {capacidad} {método} {ruta}</c>) y la deja pasar.</summary>
    private sealed class ConRegistro(IHttpClientFactory real, ILogger log) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var interno = real.CreateClient(name);
            return new HttpClient(new Cable(interno, name, log)) { BaseAddress = interno.BaseAddress };
        }

        private sealed class Cable(HttpClient interno, string capacidad, ILogger log) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                log.LogInformation("Llamada {Capacidad} {Metodo} {Ruta}", capacidad, r.Method.Method, r.RequestUri!.AbsolutePath);
                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await interno.SendAsync(copia, ct);
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
}
