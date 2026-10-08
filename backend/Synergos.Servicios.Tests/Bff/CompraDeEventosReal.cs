using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Contracts;
using Synergos.CMS.Tests.Architecture;   // Proyectos: la raíz del repo, resuelta del disco (#136)

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La compra de entradas DE VERDAD: el <c>Program</c> de <c>Bff.Eventos</c> contra Pricing, Inventory,
/// Payments y Notifications reales en el arnés, con lo que pone la puerta en cabeceras (ADR 0140 F3).
/// </summary>
/// <remarks>
/// Lo comparten los tests que miran lo que queda —el cobro, la entrega, el fichero de la saga, el log—
/// y los que miran de quién es cada compra. Un doble que contesta 201 a lo que sea no ve ninguna de
/// las dos cosas.
/// </remarks>
internal sealed class CompraDeEventosReal : IDisposable
{
    public const string Llave = "llave-de-eventos";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "compra-eventos-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<TicketPurchaseResponse> _fabrica;

    public CompraDeEventosReal(bool conRecogida = false, decimal precio = 100_000m, Func<IHttpClientFactory, IHttpClientFactory>? envolver = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Inventory.Contracts.StockItemResponse>("inventory")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(
                "notifications", conRecogida ? [("Notifications:Pickup:Directory", Buzon)] : []);
        Aprovisionar(precio).GetAwaiter().GetResult();

        var fabrica = envolver?.Invoke(Capacidades) ?? Capacidades;
        _fabrica = new WebApplicationFactory<TicketPurchaseResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("eventos:Storage:Root", Sagas);
            b.UseSetting("Eventos:ApiKey", Llave);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(fabrica);
                s.AddSingleton<ILoggerProvider>(Log);
            });
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
    }

    public ArnesDeCapacidades Capacidades { get; }

    public HttpClient Orquestador { get; }

    public LogQueGuarda Log { get; } = new();

    public string Buzon => Path.Combine(_raiz, "buzon");

    private string Sagas => Path.Combine(_raiz, "eventos");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
    }

    /// <summary>Todo lo que el orquestador escribe en su log, con las excepciones enteras.</summary>
    public sealed class LogQueGuarda : ILoggerProvider
    {
        private readonly List<string> _lineas = new();

        public IReadOnlyList<string> Lineas { get { lock (_lineas) return _lineas.ToList(); } }

        public ILogger CreateLogger(string categoryName) => new Registro(this);

        public void Dispose() { }

        private sealed class Registro(LogQueGuarda dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (dueno._lineas) dueno._lineas.Add($"{formatter(state, exception)} {exception}");
            }
        }
    }

    // ── Lo que pone la puerta ───────────────────────────────────────────────

    /// <summary>Un sujeto como lo pone la puerta: <c>eventos.comprador:&lt;id&gt;</c>.</summary>
    public static string Sujeto(string id) => $"eventos.comprador:{id}";

    /// <summary>La configuración de negocio como la pone la puerta: base64url de un JSON.</summary>
    public static string Negocio(decimal comision)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { feePercent = comision })));

    /// <summary>El contacto del aviso como lo pone la puerta.</summary>
    public static string Contacto(string correo)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correo,
            nombre = "Ana María",
            enlace = "https://sitio.ejemplo/eventos/compra?id=x",
            sitio = "Teatro de prueba",
        })));

    /// <summary>Abre una compra de dos generales con lo que pone la puerta y lo que manda el navegador.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(
        string? sujeto, string? negocio, string llave = "compra-1", object? cuerpo = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ticket-purchases")
        {
            Content = JsonContent.Create(cuerpo ?? new { eventId = "evt-1", lines = new[] { new { tier = "GEN", quantity = 2 } } }),
        };
        peticion.Headers.Add("Idempotency-Key", llave);
        if (sujeto is not null) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto);
        if (negocio is not null) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio, negocio);
        return await Enviar(peticion);
    }

    /// <summary>Una operación sobre una compra: consultar (GET), «confirm» o «cancel».</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Sobre(
        string id, string? accion, string? sujeto, string? contacto = null)
    {
        using var peticion = accion is null
            ? new HttpRequestMessage(HttpMethod.Get, $"v1/ticket-purchases/{id}")
            : new HttpRequestMessage(HttpMethod.Post, $"v1/ticket-purchases/{id}/{accion}");
        if (sujeto is not null) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto);
        if (contacto is not null) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, contacto);
        return await Enviar(peticion);
    }

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
    {
        using var r = await Orquestador.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    /// <summary>Una lista de una capacidad: sus <c>items</c>.</summary>
    public async Task<List<JsonElement>> Lista(string capacidad, string ruta)
    {
        using var http = Capacidades.CreateClient(capacidad);
        return (await http.GetFromJsonAsync<JsonElement>(ruta)).GetProperty("items").EnumerateArray().ToList();
    }

    /// <summary>El fichero de una saga, tal como lo guardó el orquestador.</summary>
    public string SagaGuardada(string id)
        => File.ReadAllText(Directory.EnumerateFiles(Path.Combine(Sagas, "sagas"), "*.json")
            .Single(f => File.ReadAllText(f).Contains($"\"id\":\"{id}\"", StringComparison.Ordinal)));

    private async Task Post(string capacidad, string ruta, object cuerpo, string llave)
    {
        using var http = Capacidades.CreateClient(capacidad);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    /// <summary>El evento a la venta: el precio de la localidad sin impuesto, su aforo y la plantilla del aviso.</summary>
    private async Task Aprovisionar(decimal precio)
    {
        await Post("pricing", "v1/prices", new
        {
            subjectKind = "eventos.localidad",
            subjectId = "evt-1/GEN",
            amount = new { amount = precio, currency = "COP" },
            taxRateBasisPoints = 0,
        }, "precio-gen");
        await Post("inventory", "v1/items", new { subjectKind = "eventos.aforo", subjectId = "evt-1/GEN", onHand = 10 }, "aforo-gen");

        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = fichero.RootElement.EnumerateArray()
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "eventos.entradas.confirmadas");
        await Post("notifications", "v1/templates", new
        {
            key = p.GetProperty("key").GetString(),
            channel = p.GetProperty("channel").GetString(),
            subject = p.GetProperty("subject").GetString(),
            body = p.GetProperty("body").GetString(),
        }, "plantilla-entradas");
    }
}
