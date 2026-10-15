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
    /// <param name="cae">Las rutas (<c>METODO /ruta</c>, «*» un segmento) que contestan 503, como una capacidad caída.</param>
    /// <param name="rechaza">Las que contestan 409 con un rechazo definitivo (no transitorio).</param>
    /// <param name="reloj">El reloj del orquestador (el del abandono); nulo, el del sistema.</param>
    /// <param name="conRecogida">Notifications deja cada correo como <c>.eml</c> en <see cref="Buzon"/>: el aviso se verifica
    /// abriendo el correo, nunca con el log.</param>
    public CitaDeSaludReal(decimal? precio = 50_000m, string? permiso = null, IReadOnlyCollection<string>? cae = null,
        IReadOnlyDictionary<string, string>? ajustes = null, TimeProvider? reloj = null, bool conRecogida = false,
        IReadOnlyCollection<string>? rechaza = null)
    {
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(
                "notifications", conRecogida ? [("Notifications:Pickup:Directory", Buzon)] : []);
        Aprovisionar(precio).GetAwaiter().GetResult();

        var cable = new Cable(Capacidades, permiso, cae ?? [], rechaza ?? []);
        _fabrica = new WebApplicationFactory<AppointmentResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("salud:Storage:Root", Raiz);
            b.UseSetting("Salud:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IHttpClientFactory>(cable);
                if (reloj is not null) s.AddSingleton(reloj);
            });
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

    public string Buzon => Path.Combine(_raiz, "buzon");

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
        // La agenda de OTRO vertical en la misma capacidad: la cita no la puede nombrar (NR-1).
        await Post("booking", "v1/resources", new { subjectKind = "realty.listado", subjectId = "casa-1", capacity = 1, timeZoneId = "UTC" }, "agenda-ajena");
        if (precio is { } p)
        {
            await Post("pricing", "v1/prices", new
            {
                subjectKind = "salud.servicio", subjectId = "consulta", amount = new { amount = p, currency = "COP" }, taxRateBasisPoints = 0,
            }, "precio");
        }
        // La plantilla de VERDAD, la que publica provisionar.sh: el correo que se abre es el que recibiría el paciente.
        using var plantillas = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Synergos.CMS.Tests.Architecture.Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        var cita = plantillas.RootElement.EnumerateArray()
            .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == "salud.cita.confirmada");
        await Post("notifications", "v1/templates", new
        {
            key = cita.GetProperty("key").GetString(), channel = cita.GetProperty("channel").GetString(),
            subject = cita.GetProperty("subject").GetString(), body = cita.GetProperty("body").GetString(),
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

    private static object Pedido(string profesionalId = "dra-1") => new
    {
        profesionalId,
        ventana = new { start = Inicio, end = Inicio.AddMinutes(30) },
    };

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(string sujeto = "u-1")
        => Enviar(ConNegocio(DeLaPuerta(HttpMethod.Post, "v1/flujos/salud.cita/cotizar", sujeto, Pedido())));

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(string llave = "cita-1", string sujeto = "u-1", string profesionalId = "dra-1")
    {
        var peticion = ConNegocio(DeLaPuerta(HttpMethod.Post, "v1/flujos/salud.cita", sujeto, Pedido(profesionalId)));
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

    public Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cancelar(string id, string sujeto = "u-1")
        => Enviar(DeLaPuerta(HttpMethod.Post, $"v1/flujos/salud.cita/{id}/cancelar", sujeto));

    /// <summary>Una vuelta del barrido de la saga genérica, como la da el proceso cada minuto.</summary>
    public Task Barrer()
    {
        var barrido = Servicios.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<CompensationSweeper<SagaDeFlujo>>().Single();
        return barrido.UnaVueltaAsync(Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>(), CancellationToken.None);
    }

    public int Veces(string llamada) => Llamadas.Count(l => l == llamada);

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
    private sealed class Cable(IHttpClientFactory real, string? permiso, IReadOnlyCollection<string> cae, IReadOnlyCollection<string> rechaza)
        : IHttpClientFactory
    {
        public List<string> Llamadas { get; } = [];

        private string? Permiso { get; } = permiso;

        private IReadOnlyCollection<string> Cae { get; } = cae;

        private IReadOnlyCollection<string> Rechaza { get; } = rechaza;

        /// <summary>«*» es un segmento de la ruta: «POST /v1/holds/*/confirm».</summary>
        private static bool Coincide(string patron, string ruta)
            => System.Text.RegularExpressions.Regex.IsMatch(ruta,
                "^" + System.Text.RegularExpressions.Regex.Escape(patron).Replace("\\*", "[^/]+", StringComparison.Ordinal) + "$");

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
                if (cable.Cae.Any(c => Coincide(c, ruta))) return Problema(HttpStatusCode.ServiceUnavailable, "guion.caida");
                if (cable.Rechaza.Any(c => Coincide(c, ruta))) return Problema(HttpStatusCode.Conflict, "guion.rechazo");

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

    [Theory]
    [InlineData("realty.listado:casa-1")]
    [InlineData("casa-1")]
    public async Task Un_miembro_no_puede_apartar_la_agenda_de_otro_vertical(string profesionalId)
    {
        // NR-1: Booking es una sola agenda para todos los verticales. La cita sólo nombra profesionales de
        // salud; lo que mande el navegador es un id, y la clase la pone el orquestador.
        using var c = new CitaDeSaludReal();

        var (estado, cuerpo) = await c.Abrir(profesionalId: profesionalId);

        Assert.NotEqual(HttpStatusCode.Created, estado);
        Assert.Equal("booking.resource_not_found", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, c.Veces("POST /v1/holds"));
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

    private sealed class RelojFalso(DateTimeOffset inicio) : TimeProvider
    {
        private DateTimeOffset _ahora = inicio;
        public override DateTimeOffset GetUtcNow() => _ahora;
        public void Avanzar(TimeSpan d) => _ahora += d;
    }

    private static async Task<(string Apartado, string Cobro)> Reservado(CitaDeSaludReal c, string id)
    {
        var saga = c.Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>().Find(id)!;
        return (saga.Datos["apartadoId"].Valor!.Value.GetString()!, saga.Datos["cobroId"].Valor!.Value.GetString()!);
    }

    [Fact]
    public async Task Una_cita_abandonada_se_deshace_a_los_15_minutos_y_no_antes()
    {
        // T018 (S-13): el apartado de Booking vence a los 10 minutos; la autorización del copago no vence
        // sola. Con el plazo de 60 de siempre, quedaba 50 minutos reservada en la tarjeta del paciente.
        var reloj = new RelojFalso(DateTimeOffset.UtcNow);
        using var c = new CitaDeSaludReal(reloj: reloj);
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var (apartado, cobro) = await Reservado(c, id);

        reloj.Avanzar(TimeSpan.FromMinutes(14));
        await c.Barrer();
        Assert.Equal("Running", (await c.Consultar(id)).Cuerpo.GetProperty("estado").GetString());

        reloj.Avanzar(TimeSpan.FromMinutes(2));
        await c.Barrer();

        Assert.Equal("Compensated", (await c.Consultar(id)).Cuerpo.GetProperty("estado").GetString());
        Assert.True((await c.Leer("booking", $"v1/holds/{apartado}")).GetProperty("released").GetBoolean());
        Assert.Equal("Voided", (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
    }

    [Fact]
    public async Task Cancelar_por_la_puerta_suelta_la_franja_y_anula_y_una_confirmada_no_se_cancela()
    {
        // T019: cancelar es deshacer una cita PENDIENTE. Una confirmada se anula con su política (la ficha
        // F4), no con una compensación que devolvería el copago saltándosela.
        using var c = new CitaDeSaludReal();
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var (apartado, cobro) = await Reservado(c, id);

        var (cancelo, cancelada) = await c.Cancelar(id);

        Assert.True(cancelo == HttpStatusCode.OK, cancelada.ToString());
        Assert.Equal("Compensated", cancelada.GetProperty("estado").GetString());
        Assert.True((await c.Leer("booking", $"v1/holds/{apartado}")).GetProperty("released").GetBoolean());
        Assert.Equal("Voided", (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());

        var (_, otra) = await c.Abrir(llave: "cita-2", sujeto: "u-2");
        var otraId = otra.GetProperty("id").GetString()!;
        await c.Cerrar(otraId, sujeto: "u-2");
        var (noCancelo, _) = await c.Cancelar(otraId, sujeto: "u-2");

        Assert.NotEqual(HttpStatusCode.OK, noCancelo);
        Assert.Equal("Completed", (await c.Consultar(otraId, sujeto: "u-2")).Cuerpo.GetProperty("estado").GetString());
    }

    [Fact]
    public async Task Una_cita_ajena_no_existe_y_repetir_no_aparta_ni_cobra_dos_veces()
    {
        // T020 (US4): la misma intención es la misma cita; la de otro, la misma respuesta que una que no existe.
        using var c = new CitaDeSaludReal();
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound, (await c.Consultar(id, sujeto: "u-2")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Cerrar(id, sujeto: "u-2")).Estado);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Cancelar(id, sujeto: "u-2")).Estado);

        var (_, otraVez) = await c.Abrir();
        Assert.Equal(id, otraVez.GetProperty("id").GetString());
        Assert.Equal(1, c.Veces("POST /v1/holds"));

        await c.Cerrar(id);
        var (_, segunda) = await c.Cerrar(id);
        Assert.Equal("Completed", segunda.GetProperty("estado").GetString());
        Assert.Equal(1, c.Llamadas.Count(l => l.EndsWith("/capture", StringComparison.Ordinal)));
        Assert.Equal(1, (await c.Leer("notifications", "v1/deliveries?toKind=salud.paciente&toId=u-1")).GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task El_correo_de_la_cita_lleva_numero_total_y_enlace_y_el_contacto_no_queda_en_la_saga()
    {
        // T024 (US5): se abre el .eml, no el log. La plantilla es la de provisionar.plantillas.json.
        using var c = new CitaDeSaludReal(conRecogida: true);
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var numero = abierta.GetProperty("numero").GetString()!;

        var (cerro, _) = await c.Cerrar(id);

        Assert.Equal(HttpStatusCode.OK, cerro);
        var eml = File.ReadAllText(Assert.Single(Directory.GetFiles(c.Buzon, "*.eml")));
        var (asunto, cuerpo) = Correo(eml);
        Assert.Contains("paciente@ejemplo.co", eml, StringComparison.Ordinal);
        Assert.Contains(numero, asunto, StringComparison.Ordinal);
        Assert.Contains("50.000 COP", cuerpo, StringComparison.Ordinal);
        Assert.Contains($"healthcare/?cita={id}", cuerpo, StringComparison.Ordinal);

        var guardado = string.Concat(Directory.EnumerateFiles(Path.Combine(c.Raiz, "flujos"), "*", SearchOption.AllDirectories).Select(File.ReadAllText));
        Assert.Contains(id, guardado, StringComparison.Ordinal);
        Assert.DoesNotContain("paciente@ejemplo.co", guardado, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Si_la_captura_falla_se_anula_la_autorizacion_y_se_suelta_la_franja()
    {
        // US3: antes de mover plata, deshacer es barato — anular, no devolver.
        using var c = new CitaDeSaludReal(rechaza: ["POST /v1/payments/*/capture"]);
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var (apartado, cobro) = await Reservado(c, id);

        var (cerro, _) = await c.Cerrar(id);

        Assert.NotEqual(HttpStatusCode.OK, cerro);
        Assert.Equal("Compensated", (await c.Consultar(id)).Cuerpo.GetProperty("estado").GetString());
        Assert.Equal("Voided", (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.True((await c.Leer("booking", $"v1/holds/{apartado}")).GetProperty("released").GetBoolean());
        Assert.DoesNotContain(c.Llamadas, l => l.EndsWith("/refund", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Si_confirmar_la_franja_falla_tras_cobrar_se_devuelve_el_copago_entero()
    {
        // US3, el caso que justifica todo: se cobró y la cita no existe. Capturar va ANTES de confirmar
        // justamente para que este fallo deje plata cobrada sin cita, que se devuelve sola.
        using var c = new CitaDeSaludReal(rechaza: ["POST /v1/holds/*/confirm"]);
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var (apartado, cobro) = await Reservado(c, id);

        var (cerro, _) = await c.Cerrar(id);

        Assert.NotEqual(HttpStatusCode.OK, cerro);
        Assert.Equal("Compensated", (await c.Consultar(id)).Cuerpo.GetProperty("estado").GetString());
        Assert.Equal(0m, (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("refundable").GetProperty("amount").GetDecimal());
        Assert.True((await c.Leer("booking", $"v1/holds/{apartado}")).GetProperty("released").GetBoolean());
        Assert.Equal(1, c.Llamadas.Count(l => l.EndsWith("/refund", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Si_el_aviso_no_sale_la_cita_sigue_confirmada_y_el_copago_cobrado()
    {
        // Un correo que no sale no devuelve la plata (al_fallar: seguir): la cita existe y está pagada.
        using var c = new CitaDeSaludReal(cae: ["POST /v1/deliveries"]);
        var (_, abierta) = await c.Abrir();
        var id = abierta.GetProperty("id").GetString()!;
        var (_, cobro) = await Reservado(c, id);

        var (cerro, cerrada) = await c.Cerrar(id);

        Assert.True(cerro == HttpStatusCode.OK, cerrada.ToString());
        Assert.Equal("Completed", cerrada.GetProperty("estado").GetString());
        Assert.Equal("Captured", (await c.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString());
        Assert.DoesNotContain(c.Llamadas, l => l.EndsWith("/refund", StringComparison.Ordinal));
    }

    /// <summary>El asunto y el cuerpo de un .eml de la recogida: asunto en palabras codificadas UTF-8, cuerpo en Base64.</summary>
    private static (string Asunto, string Cuerpo) Correo(string eml)
    {
        var corte = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var cabeceras = eml[..corte];
        var asunto = System.Text.RegularExpressions.Regex.Match(cabeceras, @"(?im)^Subject:\s*(.+(?:\r\n[ \t].+)*)").Groups[1].Value;
        asunto = System.Text.RegularExpressions.Regex.Replace(asunto, @"=\?utf-8\?B\?([^?]+)\?=\s*",
            m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value)), System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var cuerpo = Encoding.UTF8.GetString(Convert.FromBase64String(string.Concat(eml[(corte + 4)..].Where(ch => !char.IsWhiteSpace(ch)))));
        return (asunto, cuerpo);
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
