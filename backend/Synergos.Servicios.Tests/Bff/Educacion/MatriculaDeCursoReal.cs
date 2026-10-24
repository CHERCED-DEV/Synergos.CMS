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
using Synergos.Bff.Educacion.Contracts;
using Synergos.CMS.Tests.Architecture;

namespace Synergos.CMS.Tests.Bff.Educacion;

/// <summary>
/// La matrícula DE VERDAD (010-educacion-matricula): el <c>Program</c> de <c>Bff.Educacion</c> contra precios, pagos y
/// avisos reales del arnés, con lo que pone la puerta en cabeceras.
/// </summary>
/// <remarks>
/// <para>El curso se publica por la RUTA DE OFERTAS del propio orquestador, como lo hará el CMS: así la matrícula sólo
/// encuentra los precios que la oferta dejó.</para>
///
/// <para>Lo que ninguna capacidad real sabe hacer a pedido (rechazar un cobro, caerse) se inyecta en el cable con
/// <c>falla</c>, sin tocar el orquestador.</para>
/// </remarks>
internal sealed class MatriculaDeCursoReal : IDisposable
{
    public const string Llave = "llave-de-educacion";
    public const string Curso = "gestion-agil";
    public const decimal Precio = 320_000m;
    public const decimal Premium = 345_600m;

    /// <summary>La categoría con que el arnés registra cada llamada a una capacidad.</summary>
    public const string LlamadasAlArnes = "Synergos.Arnes.Llamadas";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "educacion-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<OfertaDeCursoResponse> _fabrica;

    public MatriculaDeCursoReal(
        Func<HttpRequestMessage, string?>? falla = null, IReadOnlyDictionary<string, string>? ajustes = null,
        ILoggerProvider? registro = null, TimeProvider? reloj = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");
        PublicarPlantilla().GetAwaiter().GetResult();

        IHttpClientFactory fabrica = falla is null ? Capacidades : new ConFallo(Capacidades, falla);
        if (registro is not null) fabrica = new ConRegistro(fabrica, registro.CreateLogger(LlamadasAlArnes));
        _fabrica = new WebApplicationFactory<OfertaDeCursoResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("educacion:Storage:Root", Almacen);
            b.UseSetting("Educacion:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton(fabrica);
                if (reloj is not null) s.AddSingleton(reloj);
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
    public string Almacen => Path.Combine(_raiz, "educacion");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal con un fichero aún abierto */ }
    }

    // ── La oferta ───────────────────────────────────────────────────────────

    /// <summary>La oferta de prueba: el curso a 320.000 COP, de contado al mismo precio y «premium» a 345.600.</summary>
    public static object Oferta(string curso = Curso, decimal precio = Precio, params (string Codigo, decimal Total)[] planes)
        => new
        {
            curso,
            precio,
            moneda = "COP",
            planes = (planes.Length == 0 ? [("full", precio), ("premium", Premium)] : planes)
                .Select(p => new { codigo = p.Codigo, total = p.Total }).ToList(),
        };

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Publicar(object? oferta = null, string llave = "oferta-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas") { Content = JsonContent.Create(oferta ?? Oferta()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Retirar(string curso = Curso, string llave = "retiro-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/ofertas/{curso}/retirar");
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    /// <summary>Publica la oferta de prueba y falla si no queda publicada.</summary>
    public async Task<MatriculaDeCursoReal> ConOferta(object? oferta = null)
    {
        var (estado, cuerpo) = await Publicar(oferta);
        Assert.True(estado == HttpStatusCode.OK, $"La oferta no se publicó: {(int)estado} {cuerpo}");
        return this;
    }

    // ── Lo que hace el alumno, por la puerta ───────────────────────────────

    public static string Sujeto(string id) => $"educacion.alumno:{id}";

    /// <summary>El cuerpo de abrir o cotizar: el curso es una referencia con la clase que declara la definición (NR-1).</summary>
    public static object Cuerpo(string curso = Curso, string? plan = "full", string clase = "educacion.curso")
        => new { curso = new { kind = clase, id = curso }, plan };

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "matricula-gestion-agil", string alumno = "u-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/educacion.matricula") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(alumno));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(string alumno = "u-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/educacion.matricula/cotizar") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(alumno));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string alumno = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/educacion.matricula/{id}/cerrar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(alumno));
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { correo = "ana@ejemplo.co", nombre = "Ana", enlace = "https://sitio.ejemplo/educacion/?matricula=x", sitio = "Academia de prueba" }))));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string alumno = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, $"v1/flujos/educacion.matricula/{id}");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(alumno));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string alumno = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/educacion.matricula/{id}/cancelar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(alumno));
        return Enviar(peticion);
    }

    /// <summary>Las matrículas del sujeto que pone la puerta (lo que el CMS lee para reconciliar).</summary>
    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Listar(string consulta = "", string alumno = "u-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Get, "v1/flujos/educacion.matricula" + consulta);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(alumno));
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

    /// <summary>El estado del cobro en la capacidad de pagos.</summary>
    public async Task<string> Cobro(string saga)
        => (await Leer("payments", $"v1/payments/{Dato(saga, "cobroId")}")).GetProperty("status").GetString()!;

    /// <summary>Cuántos avisos salieron para el alumno.</summary>
    public async Task<int> Avisos(string alumno = "u-1")
        => (await Leer("notifications", $"v1/deliveries?toKind=educacion.alumno&toId={alumno}")).GetProperty("items").GetArrayLength();

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
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "educacion.matricula.confirmada");
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
        peticion.Headers.Add("Idempotency-Key", "plantilla-educacion");
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

    /// <summary>
    /// Contesta en el cable lo que <c>falla</c> devuelva: un código de negocio es un 409 con ese código; <c>"caida"</c>
    /// es un 503 sin cuerpo (una capacidad que no contesta). Nulo: deja pasar.
    /// </summary>
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
}
