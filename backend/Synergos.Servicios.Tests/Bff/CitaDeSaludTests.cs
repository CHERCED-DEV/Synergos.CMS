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
using Synergos.Bff.Salud.Contracts;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La cita DE VERDAD por el flujo declarado (003-salud-cita): el <c>Program</c> de <c>Bff.Salud</c> sirviendo
/// <c>salud.cita</c> contra Booking, Pricing, Payments y Notifications reales en el arnés, con lo que pone la
/// puerta en cabeceras.
/// </summary>
/// <remarks>
/// <b>El permiso de agenda va guionado en el cable</b>: otorgarlo en <c>Api.Consent</c> exige identidad
/// resuelta (P3, la ficha del consentimiento), así que lo que contesta <c>grants/check</c> se elige por prueba.
/// Lo demás son las capacidades reales; lo que ninguna sabe hacer a pedido (caerse) también va en el cable.
/// </remarks>
internal sealed class CitaDeSaludReal : IDisposable
{
    public const string Llave = "llave-de-salud";
    public static readonly DateTimeOffset Inicio = new(DateTimeOffset.UtcNow.Date.AddDays(10).AddHours(15), TimeSpan.Zero);

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "cita-salud-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<AppointmentResponse> _fabrica;

    /// <param name="precio">El copago publicado; nulo, sin precio publicado.</param>
    /// <param name="permiso">Nulo: el permiso está vigente. Un código: <c>grants/check</c> lo rechaza con él.</param>
    /// <param name="cae">Las rutas (<c>METODO /ruta</c>) que contestan 503, como una capacidad caída.</param>
    public CitaDeSaludReal(decimal? precio = 50_000m, string? permiso = null, IReadOnlyCollection<string>? cae = null,
        IReadOnlyDictionary<string, string>? ajustes = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>("notifications");
        Aprovisionar(precio).GetAwaiter().GetResult();

        var cable = new Cable(Capacidades, permiso, cae ?? []);
        _fabrica = new WebApplicationFactory<AppointmentResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("salud:Storage:Root", Raiz);
            b.UseSetting("Salud:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s => s.AddSingleton<IHttpClientFactory>(cable));
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
        Llamadas = cable.Llamadas;
    }

    public ArnesDeCapacidades Capacidades { get; }

    public HttpClient Orquestador { get; }

    public IServiceProvider Servicios => _fabrica.Services;

    /// <summary>Lo que el orquestador pidió a las capacidades, <c>METODO /ruta</c>.</summary>
    public IReadOnlyList<string> Llamadas { get; }

    public string Raiz => Path.Combine(_raiz, "salud");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* un temporal */ }
    }

    private async Task Aprovisionar(decimal? precio)
    {
        await Post("booking", "v1/resources", new { subjectKind = "salud.profesional", subjectId = "dra-1", capacity = 1, timeZoneId = "UTC" }, "agenda");
        if (precio is { } p)
        {
            await Post("pricing", "v1/prices", new
            {
                subjectKind = "salud.servicio", subjectId = "consulta", amount = new { amount = p, currency = "COP" }, taxRateBasisPoints = 0,
            }, "precio");
        }
        await Post("notifications", "v1/templates", new
        {
            key = "salud.cita.confirmada", channel = "Email",
            subject = "Tu cita {numero} quedó confirmada",
            body = "<p>Hola {nombre}: tu cita {numero} en {sitio} quedó confirmada. Copago: {total}. {enlace}</p>",
        }, "plantilla");
    }

    private async Task Post(string capacidad, string ruta, object cuerpo, string llave)
    {
        using var http = Capacidades.CreateClient(capacidad);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    private static string Base64(object valor) => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(valor)));

    private static HttpRequestMessage DeLaPuerta(HttpMethod metodo, string ruta, string sujeto, object? cuerpo = null)
    {
        var peticion = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, $"salud.paciente:{sujeto}");
        return peticion;
    }

    private static HttpRequestMessage ConNegocio(HttpRequestMessage peticion)
    {
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Negocio, Base64(new { servicio = new { kind = "salud.servicio", id = "consulta" } }));
        return peticion;
    }

    private static object Pedido() => new
    {
        profesional = new { kind = "salud.profesional", id = "dra-1" },
        ventana = new { start = Inicio, end = Inicio.AddMinutes(30) },
    };

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(string sujeto = "u-1")
        => Enviar(ConNegocio(DeLaPuerta(HttpMethod.Post, "v1/flujos/salud.cita/cotizar", sujeto, Pedido())));

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "cita-1", string sujeto = "u-1")
    {
        var peticion = ConNegocio(DeLaPuerta(HttpMethod.Post, "v1/flujos/salud.cita", sujeto, Pedido()));
        peticion.Headers.Add("Idempotency-Key", llave);
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cerrar(string id, string sujeto = "u-1")
    {
        var peticion = DeLaPuerta(HttpMethod.Post, $"v1/flujos/salud.cita/{id}/cerrar", sujeto);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, Base64(new
        {
            correo = "paciente@ejemplo.co", nombre = "Ana", enlace = "https://sitio.ejemplo/healthcare/?cita=" + id, sitio = "Clínica de prueba",
        }));
        return Enviar(peticion);
    }

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Consultar(string id, string sujeto = "u-1")
        => Enviar(DeLaPuerta(HttpMethod.Get, $"v1/flujos/salud.cita/{id}", sujeto));

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

    /// <summary>Las capacidades reales, con el permiso guionado y las caídas pedidas.</summary>
    private sealed class Cable(IHttpClientFactory real, string? permiso, IReadOnlyCollection<string> cae) : IHttpClientFactory
    {
        public List<string> Llamadas { get; } = [];

        private string? Permiso { get; } = permiso;

        private IReadOnlyCollection<string> Cae { get; } = cae;

        public HttpClient CreateClient(string name)
        {
            var interno = name == "consent" ? null : real.CreateClient(name);
            return new HttpClient(new Paso(this, interno)) { BaseAddress = interno?.BaseAddress ?? new Uri("http://consent.local/") };
        }

        private sealed class Paso(Cable cable, HttpClient? interno) : HttpMessageHandler
        {
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                var ruta = $"{r.Method.Method} {r.RequestUri!.AbsolutePath}";
                lock (cable.Llamadas) cable.Llamadas.Add(ruta);

                if (ruta == "POST /v1/grants/check")
                {
                    return cable.Permiso is null
                        ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { id = "g1", active = true }) }
                        : Problema(HttpStatusCode.Forbidden, cable.Permiso);
                }
                if (cable.Cae.Contains(ruta)) return Problema(HttpStatusCode.ServiceUnavailable, "guion.caida");

                using var copia = new HttpRequestMessage(r.Method, r.RequestUri) { Content = r.Content };
                foreach (var h in r.Headers) copia.Headers.TryAddWithoutValidation(h.Key, h.Value);
                return await interno!.SendAsync(copia, ct);
            }

            private static HttpResponseMessage Problema(HttpStatusCode estado, string codigo) => new(estado)
            {
                Content = new StringContent($$"""{"code":"{{codigo}}","detail":"guionado en el cable"}""", Encoding.UTF8, "application/problem+json"),
            };
        }
    }
}

/// <summary>Las historias 1, 2 y 3 de la cita por el flujo declarado, contra las capacidades reales.</summary>
public sealed class CitaDeSaludTests
{
    [Fact]
    public async Task Agenda_aparta_y_autoriza_confirma_captura_y_confirma_la_franja()
    {
        using var c = new CitaDeSaludReal();

        var (estado, abierta) = await c.Abrir();
        Assert.True(estado == HttpStatusCode.Created, abierta.ToString());
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());
        Assert.Equal(50_000m, abierta.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        var id = abierta.GetProperty("id").GetString()!;

        var (cerro, cerrada) = await c.Cerrar(id);
        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());

        var reserva = cerrada.GetProperty("datos").GetProperty("reservaId").GetString()!;
        Assert.Equal("Confirmed", (await c.Leer("booking", $"v1/reservations/{reserva}")).GetProperty("status").GetString());
        var cobro = c.Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>().Find(id)!.Datos["cobroId"].Valor!.Value.GetString()!;
        Assert.Equal("Captured", (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.Equal(1, (await c.Leer("notifications", "v1/deliveries?toKind=salud.paciente&toId=u-1")).GetProperty("items").GetArrayLength());

        // El orden del cierre lo fija el oráculo: capturar ANTES de confirmar la franja.
        Assert.True(c.Llamadas.ToList().FindIndex(l => l.EndsWith("/capture", StringComparison.Ordinal))
            < c.Llamadas.ToList().FindIndex(l => l.StartsWith("POST /v1/holds/", StringComparison.Ordinal) && l.EndsWith("/confirm", StringComparison.Ordinal)));

        var (_, consultada) = await c.Consultar(id);
        Assert.Equal("Completed", consultada.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Cotizar_dice_el_copago_que_se_autoriza_sin_apartar_nada()
    {
        using var c = new CitaDeSaludReal();

        var (estado, cotizada) = await c.Cotizar();

        Assert.True(estado == HttpStatusCode.OK, cotizada.ToString());
        Assert.Equal(50_000m, cotizada.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        Assert.DoesNotContain(c.Llamadas, l => l == "POST /v1/holds" || l == "POST /v1/payments");
    }

    [Fact]
    public async Task Sin_precio_publicado_la_cita_no_cobra_y_se_confirma()
    {
        using var c = new CitaDeSaludReal(precio: null);

        var (_, abierta) = await c.Abrir();
        Assert.Equal(0m, abierta.GetProperty("datos").GetProperty("total").GetProperty("amount").GetDecimal());
        var (_, cerrada) = await c.Cerrar(abierta.GetProperty("id").GetString()!);

        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.DoesNotContain(c.Llamadas, l => l == "POST /v1/payments");
    }

    [Fact]
    public async Task Con_precios_caido_no_se_agenda_gratis_ni_se_aparta_nada()
    {
        // P2 (lote 2): sólo la AUSENCIA de precio es cero; una caída rechaza, y antes de apartar.
        using var c = new CitaDeSaludReal(cae: ["POST /v1/quotes"]);

        var (estado, cuerpo) = await c.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("guion.caida", cuerpo.GetProperty("code").GetString());
        Assert.DoesNotContain(c.Llamadas, l => l == "POST /v1/holds" || l == "POST /v1/payments");
    }

    [Fact]
    public async Task Sin_permiso_de_agenda_se_rechaza_antes_de_tocar_nada()
    {
        using var c = new CitaDeSaludReal(permiso: "consent.not_granted");

        var (estado, cuerpo) = await c.Abrir();

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("consent.not_granted", cuerpo.GetProperty("code").GetString());
        Assert.DoesNotContain(c.Llamadas, l => l == "POST /v1/holds" || l == "POST /v1/payments" || l == "POST /v1/quotes");
    }

    [Fact]
    public async Task La_saga_declarada_vive_en_su_carpeta_y_no_en_la_de_las_citas_imperativas()
    {
        // NN-15: las dos máquinas comparten raíz; la genérica va en flujos/ para que ningún barrido lea la otra.
        using var c = new CitaDeSaludReal();

        var (_, abierta) = await c.Abrir();
        Assert.Equal("Running", abierta.GetProperty("estado").GetString());

        Assert.True(Directory.Exists(Path.Combine(c.Raiz, "flujos", "sagas")), "la saga declarada no está en flujos/sagas");
        Assert.False(Directory.Exists(Path.Combine(c.Raiz, "sagas")), "la saga declarada cayó en la carpeta de las imperativas");
    }

    [Fact]
    public async Task Con_el_interruptor_en_imperativo_la_puerta_no_tiene_cita_y_las_rutas_viejas_siguen()
    {
        using var c = new CitaDeSaludReal(ajustes: new Dictionary<string, string> { ["salud:Flujos:Modo"] = "imperativo" });

        var (estado, _) = await c.Abrir();
        using var vieja = await c.Orquestador.GetAsync("v1/appointments/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, estado);
        Assert.Equal(HttpStatusCode.NotFound, vieja.StatusCode);
        Assert.Contains("salud.appointment_not_found", await vieja.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}

/// <summary>La llave compartida sale UNA vez aunque las dos máquinas configuren el mismo cliente (NN-15).</summary>
public sealed class LlaveUnaVezEnSaludTests
{
    [Theory]
    [InlineData("booking")]
    [InlineData("pricing")]
    [InlineData("payments")]
    [InlineData("notifications")]
    public void Cada_cliente_comun_lleva_la_llave_una_sola_vez(string capacidad)
    {
        using var fabrica = new WebApplicationFactory<AppointmentResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("salud:Storage:Root", Path.Combine(Path.GetTempPath(), "llave-salud-" + Guid.NewGuid().ToString("N")));
            b.UseSetting($"salud:Capabilities:{capacidad}:ApiKey", "k-" + capacidad);
        });

        using var cliente = fabrica.Services.GetRequiredService<IHttpClientFactory>().CreateClient(capacidad);

        Assert.True(cliente.DefaultRequestHeaders.TryGetValues(SharedKeyAuth.HeaderName, out var valores));
        Assert.Equal(["k-" + capacidad], valores);
    }
}
