using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Gobierno.Contracts;
using Synergos.CMS.Tests.Architecture;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff.Gobierno;

/// <summary>
/// La radicación DE VERDAD (009-gobierno-tramite): el <c>Program</c> de <c>Bff.Gobierno</c> contra precios, pagos,
/// avisos y procesos reales del arnés, con lo que pone la puerta en cabeceras.
/// </summary>
/// <remarks>
/// <para>Las definiciones de proceso se publican desde <c>tools/provisionar.procesos.json</c>, el fichero que declara el
/// repo, y el orquestador lee ese mismo fichero como su catálogo (<c>gobierno:Procesos:Archivo</c>): la v2 que arranca
/// la radicación es la que se provisiona.</para>
///
/// <para>El trámite se publica por la RUTA DE OFERTAS del propio orquestador, como lo hará el CMS: la radicación sólo
/// encuentra la tasa que la oferta dejó. La plantilla del aviso, del fichero que la aprovisiona.</para>
///
/// <para>Lo que ninguna capacidad real sabe hacer a pedido (rechazar un cobro, caerse) se inyecta en el cable con
/// <c>falla</c>, sin tocar el orquestador; y el cable anota cada llamada con su correlación.</para>
/// </remarks>
internal sealed class RadicacionReal : IDisposable
{
    public const string Llave = "llave-de-gobierno";
    public const string Tramite = "tram-certificado-residencia";
    public const decimal Tasa = 65_500m;
    public const string VersionDelTexto = "2026-10-v1";

    /// <summary>La categoría con que el cable registra cada llamada a una capacidad.</summary>
    public const string LlamadasAlArnes = "Synergos.Arnes.Llamadas";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "gobierno-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<OfertaDeTramiteResponse> _fabrica;

    public RadicacionReal(
        IReadOnlyDictionary<string, string>? ajustes = null, Func<HttpRequestMessage, string?>? falla = null,
        ILoggerProvider? registro = null, TimeProvider? reloj = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications")
            .Levanta<Synergos.Api.Workflow.Contracts.InstanceResponse>("workflow");
        PublicarProcesos().GetAwaiter().GetResult();
        PublicarPlantilla().GetAwaiter().GetResult();

        Cable = new CableDeGobierno(Capacidades, falla, registro?.CreateLogger(LlamadasAlArnes));
        _fabrica = new WebApplicationFactory<OfertaDeTramiteResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("gobierno:Storage:Root", Almacen);
            b.UseSetting("Gobierno:ApiKey", Llave);
            b.UseSetting("gobierno:Procesos:Archivo", Procesos);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IHttpClientFactory>(Cable);
                if (reloj is not null) s.AddSingleton(reloj);
                if (registro is not null) s.AddLogging(l => l.AddProvider(registro).SetMinimumLevel(LogLevel.Trace));
            });
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
    }

    /// <summary>El catálogo de procesos que declara el repo: el que publica la provisión.</summary>
    public static string Procesos => Path.Combine(Proyectos.Raiz(), "tools", "provisionar.procesos.json");

    public ArnesDeCapacidades Capacidades { get; }

    /// <summary>El cable por el que el orquestador llama a las capacidades: lo que pidió, en orden.</summary>
    public CableDeGobierno Cable { get; }

    public HttpClient Orquestador { get; }

    public IServiceProvider Servicios => _fabrica.Services;

    public SagaEngine<SagaDeFlujo> Sagas => Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>();

    /// <summary>La raíz del almacén del orquestador (sus sagas y el rastro de sus ofertas).</summary>
    public string Almacen => Path.Combine(_raiz, "gobierno");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal con un fichero aún abierto */ }
    }

    // ── La oferta ───────────────────────────────────────────────────────────

    public static object Oferta(string tramite = Tramite, decimal tasa = Tasa, string moneda = "COP")
        => new { tramite, tasa, moneda };

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(object? oferta = null, string llave = "oferta-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta ?? Oferta()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string tramite = Tramite, string llave = "retiro-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{tramite}/retirar");
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    /// <summary>Publica la oferta de prueba y falla si no queda publicada.</summary>
    public async Task<RadicacionReal> ConOferta(object? oferta = null)
    {
        var (estado, cuerpo) = await Publicar(oferta);
        Assert.True(estado == HttpStatusCode.OK, $"La oferta no se publicó: {(int)estado} {cuerpo}");
        return this;
    }

    // ── Lo que hace el ciudadano, por la puerta ─────────────────────────────

    public static string Sujeto(string id) => $"gobierno.ciudadano:{id}";

    /// <summary>El cuerpo de abrir o cotizar: el trámite es una referencia con la clase que declara la definición (NR-1).</summary>
    public static object Cuerpo(string tramite = Tramite, bool acepta = true, string clase = "gobierno.tramite")
        => new { tramite = new { kind = clase, id = tramite }, notificacionElectronica = acepta, versionDelTexto = VersionDelTexto };

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(
        string llave = "radicacion-1", string ciudadano = "c-1", object? cuerpo = null, string? correlacion = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/gobierno.radicacion") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        if (correlacion is not null) peticion.Headers.TryAddWithoutValidation(Correlation.HeaderName, correlacion);
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(string ciudadano = "c-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/gobierno.radicacion/cotizar") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        return Enviar(peticion);
    }

    /// <summary>El correo y el nombre del contacto que la puerta manda al cerrar: no pueden aparecer en ningún registro.</summary>
    public const string Correo = "maria@ejemplo.co";
    public const string Nombre = "María";

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string ciudadano = "c-1", string? correlacion = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/gobierno.radicacion/{id}/cerrar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { correo = Correo, nombre = Nombre, enlace = "https://sitio.ejemplo/gobierno/tramites", sitio = "Alcaldía de prueba" }))));
        if (correlacion is not null) peticion.Headers.TryAddWithoutValidation(Correlation.HeaderName, correlacion);
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string ciudadano = "c-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/gobierno.radicacion/{id}");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string ciudadano = "c-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/gobierno.radicacion/{id}/cancelar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        return Enviar(peticion);
    }

    /// <summary>Las radicaciones del sujeto que pone la puerta.</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Listar(string ciudadano = "c-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, "v1/flujos/gobierno.radicacion");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        return Enviar(peticion);
    }

    /// <summary>Una vuelta del barrido de abandono, como la da el proceso cada minuto.</summary>
    public Task Barrer()
        => Servicios.GetServices<IHostedService>().OfType<CompensationSweeper<SagaDeFlujo>>().Single()
            .UnaVueltaAsync(Sagas, CancellationToken.None);

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

    /// <summary>El cobro de la radicación, tal como está en la capacidad de pagos.</summary>
    public Task<JsonElement> Cobro(string saga) => Leer("payments", $"v1/payments/{Dato(saga, "cobroId")}");

    /// <summary>Cuántos cobros hay en pagos PARA la radicación (el origen de la saga): el invariante, no las llamadas.</summary>
    public async Task<int> Cobros(string saga)
        => (await Leer("payments", $"v1/payments?forKind=gobierno.radicacion&forId={saga}")).GetProperty("total").GetInt32();

    /// <summary>Cuántos expedientes nacieron en procesos con la radicación de sujeto (el origen de la saga).</summary>
    public async Task<int> Expedientes(string saga)
        => (await Leer("workflow", $"v1/instances?subjectKind=gobierno.radicacion&subjectId={saga}")).GetProperty("total").GetInt32();

    /// <summary>Los avisos que salieron para el ciudadano.</summary>
    public async Task<JsonElement> Avisos(string ciudadano = "c-1")
        => (await Leer("notifications", $"v1/deliveries?toKind=gobierno.ciudadano&toId={ciudadano}")).GetProperty("items");

    public static bool Es(HttpRequestMessage r, string metodo, string patron)
        => r.Method.Method == metodo && Regex.IsMatch(r.RequestUri!.AbsolutePath, patron);

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
    {
        using (peticion)
        {
            using var r = await Orquestador.SendAsync(peticion);
            var texto = await r.Content.ReadAsStringAsync();
            return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
        }
    }

    /// <summary>Las definiciones de proceso, del fichero que las provisiona de verdad, con la llave de la provisión.</summary>
    private async Task PublicarProcesos()
    {
        using var fichero = JsonDocument.Parse(File.ReadAllText(Procesos));
        foreach (var definicion in fichero.RootElement.EnumerateArray().Where(e => e.TryGetProperty("key", out _)))
        {
            using var http = Capacidades.CreateClient("workflow");
            using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/definitions")
            {
                Content = new StringContent(definicion.GetRawText(), Encoding.UTF8, "application/json"),
            };
            peticion.Headers.Add("Idempotency-Key", "provisionar:def:" + definicion.GetProperty("key").GetString());
            using var r = await http.SendAsync(peticion);
            Assert.True(r.StatusCode == HttpStatusCode.Created, $"proceso: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
        }
    }

    /// <summary>La plantilla del aviso (T029), del fichero que la provisiona de verdad.</summary>
    private async Task PublicarPlantilla()
    {
        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var p = fichero.RootElement.EnumerateArray()
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "gobierno.tramite.radicado");
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
        peticion.Headers.Add("Idempotency-Key", "plantilla-gobierno");
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"plantilla: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }
}

/// <summary>
/// El cable entre el orquestador de Gobierno y las capacidades del arnés: anota cada llamada con la correlación en curso
/// cuando salió —la que <c>CorrelationHandler</c> pega en los clientes nombrados; aquí la fábrica es la del arnés y el
/// manejador no está en la cadena—, la escribe en el registro si lo hay (<c>Llamada {capacidad} {método} {ruta}</c>), y
/// contesta lo que <c>falla</c> devuelva: un código de negocio es un 409 con ese código; <c>"caida"</c> es un 503 sin
/// cuerpo (una capacidad que no contesta). Nulo: deja pasar.
/// </summary>
internal sealed class CableDeGobierno(IHttpClientFactory real, Func<HttpRequestMessage, string?>? falla, ILogger? log) : IHttpClientFactory
{
    private readonly List<(string Capacidad, string Metodo, string Ruta, string? Correlacion)> _llamadas = [];

    /// <summary>
    /// Lo que pidió el orquestador, en orden, sin el barrido de avisos: ése corre solo desde que arranca el host y no es
    /// de ninguna radicación (sólo él usa esas rutas; la radicación avisa con <c>POST /v1/deliveries</c>).
    /// </summary>
    public IReadOnlyList<(string Capacidad, string Metodo, string Ruta, string? Correlacion)> Llamadas
    {
        get { lock (_llamadas) return _llamadas.Where(l => !EsDelBarridoDeAvisos(l.Metodo, l.Ruta)).ToList(); }
    }

    /// <summary>Cuántas veces se pidió <paramref name="metodo"/> a una ruta que casa con <paramref name="patron"/>.</summary>
    public int Veces(string metodo, string patron) => Llamadas.Count(l => l.Metodo == metodo && Regex.IsMatch(l.Ruta, patron));

    public static bool EsDelBarridoDeAvisos(string metodo, string ruta)
        => (metodo == "GET" && ruta == "/v1/deliveries/queued")
            || (metodo == "POST" && ruta.StartsWith("/v1/deliveries/", StringComparison.Ordinal)
                && (ruta.EndsWith("/retry", StringComparison.Ordinal) || ruta.EndsWith("/give-up", StringComparison.Ordinal)));

    public HttpClient CreateClient(string name)
    {
        var interno = real.CreateClient(name);
        return new HttpClient(new Hilo(this, name, interno)) { BaseAddress = interno.BaseAddress };
    }

    private string? Anotar(string capacidad, HttpRequestMessage r)
    {
        lock (_llamadas) _llamadas.Add((capacidad, r.Method.Method, r.RequestUri!.AbsolutePath, Correlation.Current));
        log?.LogInformation("Llamada {Capacidad} {Metodo} {Ruta}", capacidad, r.Method.Method, r.RequestUri!.AbsolutePath);
        return falla?.Invoke(r);
    }

    private sealed class Hilo(CableDeGobierno cable, string capacidad, HttpClient interno) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            if (cable.Anotar(capacidad, r) is { } codigo)
            {
                return codigo == "caida"
                    ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                    : new HttpResponseMessage(HttpStatusCode.Conflict)
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
