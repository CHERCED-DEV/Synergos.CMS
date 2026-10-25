using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Gobierno.Contracts;
using Synergos.CMS.Tests.Architecture;

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
/// encuentra la tasa que la oferta dejó.</para>
/// </remarks>
internal sealed class RadicacionReal : IDisposable
{
    public const string Llave = "llave-de-gobierno";
    public const string Tramite = "tram-certificado-residencia";
    public const decimal Tasa = 65_500m;
    public const string VersionDelTexto = "2026-10-v1";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "gobierno-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<OfertaDeTramiteResponse> _fabrica;

    public RadicacionReal(IReadOnlyDictionary<string, string>? ajustes = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications")
            .Levanta<Synergos.Api.Workflow.Contracts.InstanceResponse>("workflow");
        PublicarProcesos().GetAwaiter().GetResult();

        _fabrica = new WebApplicationFactory<OfertaDeTramiteResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("gobierno:Storage:Root", Almacen);
            b.UseSetting("Gobierno:ApiKey", Llave);
            b.UseSetting("gobierno:Procesos:Archivo", Procesos);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s => s.AddSingleton<IHttpClientFactory>(Capacidades));
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add("X-Synergos-Key", Llave);
    }

    /// <summary>El catálogo de procesos que declara el repo: el que publica la provisión.</summary>
    public static string Procesos => Path.Combine(Proyectos.Raiz(), "tools", "provisionar.procesos.json");

    public ArnesDeCapacidades Capacidades { get; }

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

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "radicacion-1", string ciudadano = "c-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/gobierno.radicacion") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(string ciudadano = "c-1", object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/gobierno.radicacion/cotizar") { Content = JsonContent.Create(cuerpo ?? Cuerpo()) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string ciudadano = "c-1")
    {
        var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/flujos/gobierno.radicacion/{id}/cerrar");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, Sujeto(ciudadano));
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64Url.EncodeToString(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new { correo = "maria@ejemplo.co", nombre = "María", enlace = "https://sitio.ejemplo/gobierno/tramites", sitio = "Alcaldía de prueba" }))));
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

    // ── Lo que quedó en las capacidades ─────────────────────────────────────

    public async Task<JsonElement> Leer(string capacidad, string ruta)
    {
        using var http = Capacidades.CreateClient(capacidad);
        return await http.GetFromJsonAsync<JsonElement>(ruta);
    }

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
}
