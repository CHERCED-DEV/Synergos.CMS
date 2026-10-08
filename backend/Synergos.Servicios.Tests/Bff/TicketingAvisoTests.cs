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
/// La compra de entradas avisa al comprador como un paso del flujo, y un aviso que no sale NO devuelve
/// la plata (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>El orquestador de verdad</b> —su <c>Program</c>, su JSON, su endpoint— contra las cuatro
/// capacidades de verdad en el arnés: Pricing con el precio, Inventory con el aforo, Payments que cobra
/// y Notifications que avisa. Un doble que contesta 201 a lo que sea no vería que un aviso rechazado
/// compensa la saga, ni que el correo se cuela en el disco o en el log.</para>
///
/// <para><b>Lo que se mira es lo que queda</b>: el cobro en Payments, la entrega en Notifications, el
/// <c>.eml</c> en la carpeta, el fichero de la saga y lo que el proceso escribió en su log.</para>
/// </remarks>
public sealed class TicketingAvisoTests : IDisposable
{
    private const string Llave = "llave-de-eventos";
    private const string Correo = "ana.compradora@ejemplo.co";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "aviso-eventos-" + Guid.NewGuid().ToString("N"));

    private string Buzon => Path.Combine(_raiz, "buzon");

    private string Sagas => Path.Combine(_raiz, "eventos");

    public void Dispose()
    {
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
    }

    /// <summary>Todo lo que el orquestador escribe en su log, con las excepciones enteras.</summary>
    private sealed class LogQueGuarda : ILoggerProvider
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

    /// <summary>Cuenta cuántas veces el orquestador le pide el cliente a cada capacidad: una por llamada.</summary>
    private sealed class QueCuenta(IHttpClientFactory interno) : IHttpClientFactory
    {
        private readonly Dictionary<string, int> _pedidos = new(StringComparer.Ordinal);

        public int Pedidos(string capacidad) { lock (_pedidos) return _pedidos.GetValueOrDefault(capacidad); }

        public HttpClient CreateClient(string name)
        {
            lock (_pedidos) _pedidos[name] = _pedidos.GetValueOrDefault(name) + 1;
            return interno.CreateClient(name);
        }
    }

    private ArnesDeCapacidades Capacidades(bool conRecogida)
        => new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Inventory.Contracts.StockItemResponse>("inventory")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(
                "notifications", conRecogida ? [("Notifications:Pickup:Directory", Buzon)] : []);

    private static async Task Post(ArnesDeCapacidades arnes, string capacidad, string ruta, object cuerpo, string llave)
    {
        using var http = arnes.CreateClient(capacidad);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    /// <summary>El evento a la venta: el precio de la localidad sin impuesto, su aforo y la plantilla del aviso.</summary>
    private static async Task Aprovisionar(ArnesDeCapacidades arnes, decimal precio)
    {
        await Post(arnes, "pricing", "v1/prices", new
        {
            subjectKind = "eventos.localidad",
            subjectId = "evt-1/GEN",
            amount = new { amount = precio, currency = "COP" },
            taxRateBasisPoints = 0,
        }, "precio-gen");
        await Post(arnes, "inventory", "v1/items", new { subjectKind = "eventos.aforo", subjectId = "evt-1/GEN", onHand = 10 }, "aforo-gen");

        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = fichero.RootElement.EnumerateArray()
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "eventos.entradas.confirmadas");
        await Post(arnes, "notifications", "v1/templates", new
        {
            key = p.GetProperty("key").GetString(),
            channel = p.GetProperty("channel").GetString(),
            subject = p.GetProperty("subject").GetString(),
            body = p.GetProperty("body").GetString(),
        }, "plantilla-entradas");
    }

    private WebApplicationFactory<TicketPurchaseResponse> Orquestador(IHttpClientFactory capacidades, LogQueGuarda log)
        => new WebApplicationFactory<TicketPurchaseResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("eventos:Storage:Root", Sagas);
            b.UseSetting("Eventos:ApiKey", Llave);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(capacidades);
                s.AddSingleton<ILoggerProvider>(log);
            });
        });

    /// <summary>Lo que pone la puerta en <c>X-Synergos-Contacto</c>: base64url de un JSON.</summary>
    private static string Contacto(string correo = Correo)
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correo,
            nombre = "Ana María",
            enlace = "https://sitio.ejemplo/eventos/compra?id=x",
            sitio = "Teatro de prueba",
        })));

    /// <summary>Abre y cierra una compra de dos generales; devuelve la saga y cómo terminó.</summary>
    private static async Task<(string Id, HttpStatusCode Cierre, JsonElement Compra)> Comprar(
        HttpClient http, string? contacto, string llave = "compra-1")
    {
        using var abrir = new HttpRequestMessage(HttpMethod.Post, "v1/ticket-purchases")
        {
            Content = JsonContent.Create(new
            {
                eventId = "evt-1",
                buyerKind = "eventos.comprador",
                buyerId = "m1",
                lines = new[] { new { tier = "GEN", quantity = 2 } },
                serviceFeePercent = 0m,
            }),
        };
        abrir.Headers.Add("Idempotency-Key", llave);
        using var abierta = await http.SendAsync(abrir);
        Assert.True(abierta.StatusCode == HttpStatusCode.Created, $"abrir: {(int)abierta.StatusCode} {await abierta.Content.ReadAsStringAsync()}");
        var id = (await abierta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        using var cerrar = new HttpRequestMessage(HttpMethod.Post, $"v1/ticket-purchases/{id}/confirm");
        if (contacto is not null) cerrar.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, contacto);
        using var cerrada = await http.SendAsync(cerrar);
        return (id, cerrada.StatusCode, await cerrada.Content.ReadFromJsonAsync<JsonElement>());
    }

    private static async Task<List<JsonElement>> Lista(ArnesDeCapacidades arnes, string capacidad, string ruta)
    {
        using var http = arnes.CreateClient(capacidad);
        return (await http.GetFromJsonAsync<JsonElement>(ruta)).GetProperty("items").EnumerateArray().ToList();
    }

    private string SagaGuardada(string id)
        => File.ReadAllText(Directory.EnumerateFiles(Path.Combine(Sagas, "sagas"), "*.json")
            .Single(f => File.ReadAllText(f).Contains($"\"id\":\"{id}\"", StringComparison.Ordinal)));

    [Fact]
    public async Task Con_el_transporte_que_rechaza_la_compra_se_completa_cobrada_y_sin_devolucion()
    {
        // El transporte por defecto de Api.Notifications rechaza (transport_not_configured, no
        // transitorio). Con el aviso como un paso más, eso deshacía la saga y devolvía la plata.
        using var arnes = Capacidades(conRecogida: false);
        await Aprovisionar(arnes, 100_000m);
        var log = new LogQueGuarda();
        using var fabrica = Orquestador(arnes, log);
        using var http = fabrica.CreateClient();
        http.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);

        var (id, cierre, compra) = await Comprar(http, Contacto());

        Assert.Equal(HttpStatusCode.OK, cierre);
        Assert.Equal("Completed", compra.GetProperty("status").GetString());
        Assert.Equal(0, compra.GetProperty("pendingCompensations").GetInt32());

        var cobro = Assert.Single(await Lista(arnes, "payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());

        // Lo que la capacidad sí dejó: el intento, en Failed, con su motivo.
        var entrega = Assert.Single(await Lista(arnes, "notifications", "v1/deliveries?toKind=eventos.comprador&toId=m1"));
        Assert.Equal("Failed", entrega.GetProperty("status").GetString());

        // Y el log del orquestador nombra la saga y el código, sin la dirección.
        Assert.Contains(log.Lineas, l => l.Contains(id, StringComparison.Ordinal)
                                         && l.Contains("notifications.transport_not_configured", StringComparison.Ordinal));
        Assert.DoesNotContain(log.Lineas, l => l.Contains(Correo, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(Correo, SagaGuardada(id), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Con_la_recogida_el_aviso_le_llega_al_contacto_UNA_vez_y_no_queda_en_la_saga()
    {
        using var arnes = Capacidades(conRecogida: true);
        await Aprovisionar(arnes, 100_000m);
        var log = new LogQueGuarda();
        using var fabrica = Orquestador(arnes, log);
        using var http = fabrica.CreateClient();
        http.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);

        var (id, cierre, _) = await Comprar(http, Contacto());
        Assert.Equal(HttpStatusCode.OK, cierre);

        // Confirmar otra vez una compra completada no vuelve a correr pasos: no hay segundo aviso.
        using var otraVez = new HttpRequestMessage(HttpMethod.Post, $"v1/ticket-purchases/{id}/confirm");
        otraVez.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Contacto());
        using var repetida = await http.SendAsync(otraVez);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);

        var entrega = Assert.Single(await Lista(arnes, "notifications", "v1/deliveries?toKind=eventos.comprador&toId=m1"));
        Assert.Equal(Correo, entrega.GetProperty("address").GetString());
        Assert.Equal("Accepted", entrega.GetProperty("status").GetString());
        var eml = File.ReadAllText(Assert.Single(Directory.GetFiles(Buzon, "*.eml")));
        Assert.Contains(Correo, eml, StringComparison.Ordinal);

        Assert.DoesNotContain(Correo, SagaGuardada(id), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana María", SagaGuardada(id), StringComparison.Ordinal);
        Assert.DoesNotContain(log.Lineas, l => l.Contains(Correo, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Una_compra_gratis_se_completa_sin_una_sola_llamada_a_Payments()
    {
        // Por el orquestador, un cobro de cero daba 400 payments.zero_amount: omitir_si_cero.
        using var arnes = Capacidades(conRecogida: false);
        await Aprovisionar(arnes, 0m);
        var cuenta = new QueCuenta(arnes);
        using var fabrica = Orquestador(cuenta, new LogQueGuarda());
        using var http = fabrica.CreateClient();
        http.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);

        var (id, cierre, compra) = await Comprar(http, contacto: null);

        Assert.Equal(HttpStatusCode.OK, cierre);
        Assert.Equal("Completed", compra.GetProperty("status").GetString());
        Assert.Equal(0m, compra.GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.Equal(0, cuenta.Pedidos("payments"));
        Assert.Empty(await Lista(arnes, "payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
    }

    [Fact]
    public async Task Un_contacto_ilegible_se_rechaza_antes_de_mover_plata()
    {
        using var arnes = Capacidades(conRecogida: false);
        await Aprovisionar(arnes, 100_000m);
        using var fabrica = Orquestador(arnes, new LogQueGuarda());
        using var http = fabrica.CreateClient();
        http.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);

        var (id, cierre, rechazo) = await Comprar(http, contacto: "%%no-es-base64url%%");

        Assert.Equal(HttpStatusCode.BadRequest, cierre);
        Assert.Equal("eventos.contacto_invalido", rechazo.GetProperty("code").GetString());
        var cobro = Assert.Single(await Lista(arnes, "payments", $"v1/payments?forKind=eventos.compra&forId={id}"));
        Assert.Equal("Authorized", cobro.GetProperty("status").GetString());
    }
}
