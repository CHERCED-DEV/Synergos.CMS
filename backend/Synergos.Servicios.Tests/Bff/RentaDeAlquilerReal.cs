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
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Shared;
using static Synergos.CMS.Tests.Bff.DobleDeAlquiler;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La renta DE VERDAD (línea 005, T007): el <c>Program</c> de <c>Bff.Alquiler</c> contra Booking, Pricing,
/// Payments y Notifications reales en el arnés, con lo que pone la puerta en cabeceras.
/// </summary>
/// <remarks>
/// <para><b>El reloj del orquestador es del test</b> y empieza en el de la pared: las capacidades miran el suyo
/// (una ventana en el pasado la rechaza Booking), así que adelantarlo sólo mueve lo que decide el orquestador —el
/// abandono, lo que el barrido ve—.</para>
///
/// <para><b>Lo que ninguna real sabe hacer a pedido</b> (negar una garantía, fallar una confirmación) se inyecta
/// en el cable con <c>envolver</c>, sin tocar el host.</para>
/// </remarks>
internal sealed class RentaDeAlquilerReal : IDisposable
{
    public const string Llave = "llave-de-alquiler";
    public const string Equipo = "andamio-6m";

    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "renta-real-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<RentalResponse> _fabrica;

    /// <param name="falla">El rechazo que el cable contesta a una petición del orquestador, o null para dejarla pasar.</param>
    /// <param name="ajustes">Configuración del despliegue, encima de la de siempre.</param>
    public RentaDeAlquilerReal(Func<HttpRequestMessage, string?>? falla = null, IReadOnlyDictionary<string, string>? ajustes = null)
    {
        Reloj = new RelojFalso(DateTimeOffset.UtcNow);
        Capacidades = new ArnesDeCapacidades()
            .Levanta<Synergos.Api.Booking.Contracts.HoldResponse>("booking")
            .Levanta<Synergos.Api.Pricing.Contracts.PriceResponse>("pricing")
            .Levanta<Synergos.Api.Payments.Contracts.PaymentResponse>("payments")
            .Levanta<Synergos.Api.Notifications.Contracts.SaveTemplateRequest>(
                "notifications", [("Notifications:Pickup:Directory", Buzon)]);
        Plantillas().GetAwaiter().GetResult();

        Cable = new CableQueAnota(Capacidades, falla);
        _fabrica = new WebApplicationFactory<RentalResponse>().WithWebHostBuilder(b =>
        {
            b.UseSetting("alquiler:Storage:Root", Path.Combine(_raiz, "alquiler"));
            b.UseSetting("Alquiler:ApiKey", Llave);
            foreach (var (clave, valor) in ajustes ?? new Dictionary<string, string>()) b.UseSetting(clave, valor);
            b.ConfigureTestServices(s =>
            {
                s.AddSingleton<IHttpClientFactory>(Cable);
                s.AddSingleton<TimeProvider>(Reloj);
                s.AddSingleton<ILoggerProvider>(Log);
            });
        });
        Orquestador = _fabrica.CreateClient();
        Orquestador.DefaultRequestHeaders.Add(SharedKeyAuth.HeaderName, Llave);
    }

    public ArnesDeCapacidades Capacidades { get; }

    /// <summary>Lo que el orquestador le pidió a cada capacidad, en orden.</summary>
    public CableQueAnota Cable { get; }

    public HttpClient Orquestador { get; }

    public RelojFalso Reloj { get; }

    public CompraDeEventosReal.LogQueGuarda Log { get; } = new();

    /// <summary>El contenedor del orquestador: el motor y el barrido que corren de verdad dentro de él.</summary>
    public IServiceProvider Servicios => _fabrica.Services;

    public string Buzon => Path.Combine(_raiz, "buzon");

    public void Dispose()
    {
        Orquestador.Dispose();
        _fabrica.Dispose();
        Capacidades.Dispose();
        try { if (Directory.Exists(_raiz)) Directory.Delete(_raiz, recursive: true); }
        catch (IOException) { /* el host puede tener un fichero aún abierto; es un temporal */ }
    }

    // ── Lo que pone la puerta ───────────────────────────────────────────────

    /// <summary>Un sujeto como lo pone la puerta: <c>alquiler.arrendatario:&lt;id&gt;</c>.</summary>
    public static string Sujeto(string id = "u-1") => $"alquiler.arrendatario:{id}";

    /// <summary>El contacto del aviso como lo pone la puerta: base64url de un JSON.</summary>
    public static string Contacto(string correo = "ana@ejemplo.co")
        => Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            correo,
            nombre = "Ana María",
            enlace = "https://sitio.ejemplo/alquiler/mis-alquileres",
            sitio = "Alquiler de prueba",
        })));

    /// <summary>
    /// Una ventana de <paramref name="dias"/> días que empieza dentro de diez, a media mañana en UTC: los días se
    /// cuentan por fecha, y la de Booking la mira contra su propio reloj.
    /// </summary>
    public static (DateTimeOffset Inicio, DateTimeOffset Fin) Ventana(int dias, int desde = 10)
    {
        var inicio = new DateTimeOffset(DateTimeOffset.UtcNow.Date.AddDays(desde).AddHours(9), TimeSpan.Zero);
        return (inicio, inicio.AddDays(dias));
    }

    /// <summary>Lo que manda el navegador: el equipo, las unidades (1..N) y la ventana.</summary>
    public static object Pedido(int unidades = 2, int dias = 7, string equipo = Equipo)
    {
        var (inicio, fin) = Ventana(dias);
        return new { equipo, unidades = Enumerable.Range(1, unidades).Select(u => new { unidad = u }), ventana = new { start = inicio, end = fin } };
    }

    /// <summary>
    /// La oferta del andamio: tarifa base 40 000, desde 7 días 30 000, garantía 250 000 por unidad, tres unidades y
    /// de 1 a 30 días. Los valores distintos son a propósito: cotizar con el tramo equivocado no pasa en verde.
    /// </summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> PublicarOferta(
        decimal valorDia = 40_000m, decimal desdeSiete = 30_000m, int unidades = 3, decimal garantia = 250_000m,
        string llave = "oferta-1", string equipo = Equipo)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/ofertas")
        {
            Content = JsonContent.Create(new
            {
                equipmentId = equipo,
                currency = "COP",
                dailyRate = valorDia,
                rates = new[] { new { minDays = 7, perDay = desdeSiete } },
                depositPerUnit = garantia,
                minDays = 1,
                maxDays = 30,
                units = unidades,
            }),
        };
        peticion.Headers.Add("Idempotency-Key", llave);
        return await Enviar(peticion);
    }

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Abrir(
        string llave = "alq-1", object? pedido = null, string? sujeto = null, string? correlacion = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/alquiler.renta") { Content = JsonContent.Create(pedido ?? Pedido()) };
        peticion.Headers.Add("Idempotency-Key", llave);
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto ?? Sujeto());
        if (correlacion is not null) peticion.Headers.TryAddWithoutValidation(Correlation.HeaderName, correlacion);
        return await Enviar(peticion);
    }

    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Cotizar(object? pedido = null, string? sujeto = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/flujos/alquiler.renta/cotizar") { Content = JsonContent.Create(pedido ?? Pedido()) };
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto ?? Sujeto());
        return await Enviar(peticion);
    }

    /// <summary>Una operación sobre un alquiler: consultar (GET), <c>cerrar</c> o <c>cancelar</c>.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Sobre(
        string id, string? accion, string? sujeto = null, string? contacto = null, string? correlacion = null)
    {
        var ruta = $"v1/flujos/alquiler.renta/{Uri.EscapeDataString(id)}";
        using var peticion = accion is null
            ? new HttpRequestMessage(HttpMethod.Get, ruta)
            : new HttpRequestMessage(HttpMethod.Post, $"{ruta}/{accion}");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto ?? Sujeto());
        if (contacto is not null) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Contacto, contacto);
        if (correlacion is not null) peticion.Headers.TryAddWithoutValidation(Correlation.HeaderName, correlacion);
        return await Enviar(peticion);
    }

    /// <summary>El inquilino declara la devolución (T017): con su sujeto de la puerta.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Declarar(string id, string? sujeto = null, string? llave = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/alquileres/{Uri.EscapeDataString(id)}/declarar-devolucion");
        peticion.Headers.Add("Idempotency-Key", llave ?? $"declarar-{Guid.NewGuid():N}");
        peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto ?? Sujeto());
        return await Enviar(peticion);
    }

    /// <summary>El personal confirma la devolución con el daño (T017).</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Confirmar(string id, decimal danio, string? llave = null, string personal = "staff-1")
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/alquileres/{Uri.EscapeDataString(id)}/confirmar-devolucion")
        {
            Content = JsonContent.Create(new { damage = new { amount = danio, currency = "COP" }, staffId = personal }),
        };
        peticion.Headers.Add("Idempotency-Key", llave ?? $"confirmar-{Guid.NewGuid():N}");
        return await Enviar(peticion);
    }

    /// <summary>Cancela lo confirmado (T020): el inquilino con su sujeto, o el personal con la penalidad.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> CancelarConfirmado(
        string id, bool porPersonal, decimal penalidad = 0m, string? sujeto = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, $"v1/alquileres/{Uri.EscapeDataString(id)}/cancelar-confirmado")
        {
            Content = JsonContent.Create(new
            {
                actor = porPersonal ? "staff" : "renter",
                penalty = new { amount = penalidad, currency = "COP" },
                staffId = porPersonal ? "staff-1" : null,
            }),
        };
        peticion.Headers.Add("Idempotency-Key", $"cancelar-{Guid.NewGuid():N}");
        if (!porPersonal) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto ?? Sujeto());
        return await Enviar(peticion);
    }

    /// <summary>Los alquileres de la puerta del dueño (T024): con el sujeto que pone la puerta, o sin él.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Mios(string? sujeto = null, bool sinSujeto = false)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Get, "v1/alquileres");
        if (!sinSujeto) peticion.Headers.TryAddWithoutValidation(CabecerasDeLaPuerta.Sujeto, sujeto ?? Sujeto());
        return await Enviar(peticion);
    }

    /// <summary>Pone el reloj del orquestador en esa hora de Bogotá (UTC−5) del día <paramref name="dia"/>.</summary>
    public void EnBogota(DateOnly dia, int hora, int minuto)
    {
        var destino = new DateTimeOffset(dia.ToDateTime(new TimeOnly(hora, minuto)), TimeSpan.FromHours(-5));
        Reloj.Avanzar(destino - Reloj.GetUtcNow());
    }

    /// <summary>El día de inicio de la <see cref="Ventana"/>: su fecha UTC.</summary>
    public static DateOnly DiaDeInicio(int desde = 10) => DateOnly.FromDateTime(Ventana(1, desde).Inicio.UtcDateTime);

    /// <summary>Reserva y confirma: <c>abrir</c> y <c>cerrar</c> con el contacto, como la puerta.</summary>
    public async Task<(HttpStatusCode Estado, JsonElement Cuerpo)> Rentar(string llave = "alq-1", object? pedido = null)
    {
        var abierto = await Abrir(llave, pedido);
        if (abierto.Estado != HttpStatusCode.Created) return abierto;
        return await Sobre(abierto.Cuerpo.GetProperty("id").GetString()!, "cerrar", contacto: Contacto());
    }

    private async Task<(HttpStatusCode, JsonElement)> Enviar(HttpRequestMessage peticion)
    {
        using var r = await Orquestador.SendAsync(peticion);
        var texto = await r.Content.ReadAsStringAsync();
        return (r.StatusCode, string.IsNullOrWhiteSpace(texto) ? default : JsonDocument.Parse(texto).RootElement.Clone());
    }

    // ── Lo que queda en las capacidades ─────────────────────────────────────

    /// <summary>Un recurso de una capacidad, tal como lo devuelve.</summary>
    public async Task<JsonElement> Leer(string capacidad, string ruta)
    {
        using var http = Capacidades.CreateClient(capacidad);
        return await http.GetFromJsonAsync<JsonElement>(ruta);
    }

    /// <summary>Una lista de una capacidad: sus <c>items</c>.</summary>
    public async Task<List<JsonElement>> Lista(string capacidad, string ruta)
        => (await Leer(capacidad, ruta)).GetProperty("items").EnumerateArray().ToList();

    /// <summary>Lo que Pricing cotiza HOY para esas líneas: el total.</summary>
    public async Task<decimal> Total(params (string Kind, string Id, int Cantidad)[] lineas)
    {
        using var http = Capacidades.CreateClient("pricing");
        using var r = await http.PostAsJsonAsync("v1/quotes", new { lines = lineas.Select(l => new { subjectKind = l.Kind, subjectId = l.Id, quantity = l.Cantidad }) });
        Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync());
        return (await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetProperty("amount").GetDecimal();
    }

    /// <summary>El recurso del equipo en la agenda.</summary>
    public async Task<string> Recurso(string equipo = Equipo)
        => (await Leer("booking", $"v1/resources/por-sujeto?subjectKind=alquiler.equipo&subjectId={equipo}")).GetProperty("id").GetString()!;

    /// <summary>Cuántas unidades del equipo están tomadas en la ventana de <paramref name="dias"/> días.</summary>
    public async Task<int> Tomadas(int dias = 7)
    {
        var (inicio, fin) = Ventana(dias);
        var consulta = $"start={Uri.EscapeDataString(inicio.ToString("O"))}&end={Uri.EscapeDataString(fin.ToString("O"))}";
        return (await Leer("booking", $"v1/resources/{await Recurso()}/availability?{consulta}")).GetProperty("taken").GetInt32();
    }

    // ── El motor ────────────────────────────────────────────────────────────

    public SagaEngine<SagaDeFlujo> Motor => Servicios.GetRequiredService<SagaEngine<SagaDeFlujo>>();

    /// <summary>Una vuelta del barrido de la saga declarada, como la daría el de fondo.</summary>
    public Task UnaVuelta()
        => Servicios.GetServices<IHostedService>().OfType<CompensationSweeper<SagaDeFlujo>>().Single()
            .UnaVueltaAsync(Motor, CancellationToken.None);

    private async Task Post(string capacidad, string ruta, object cuerpo, string llave)
    {
        using var http = Capacidades.CreateClient(capacidad);
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = JsonContent.Create(cuerpo) };
        peticion.Headers.Add("Idempotency-Key", llave);
        using var r = await http.SendAsync(peticion);
        Assert.True(r.StatusCode == HttpStatusCode.Created, $"{capacidad} {ruta}: {(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}");
    }

    /// <summary>
    /// Las plantillas, como las provisiona <c>tools/provisionar.plantillas.json</c>: la de la guardia y la del aviso de
    /// la renta (T030). Leídas del fichero, el correo que se mira es el que sale en un servidor de verdad.
    /// </summary>
    private async Task Plantillas()
    {
        using var fichero = JsonDocument.Parse(File.ReadAllText(Path.Combine(Architecture.Proyectos.Raiz(), "tools", "provisionar.plantillas.json")));
        foreach (var clave in new[] { "bff.compensacion.colgada", "alquiler.renta.confirmada" })
        {
            var plantilla = fichero.RootElement.EnumerateArray()
                .Single(e => e.TryGetProperty("key", out var k) && k.GetString() == clave);
            await Post("notifications", "v1/templates", new
            {
                key = clave,
                channel = plantilla.GetProperty("channel").GetString(),
                subject = plantilla.GetProperty("subject").GetString(),
                body = plantilla.GetProperty("body").GetString(),
            }, "plantilla-" + clave);
        }
    }
}

/// <summary>
/// Lo que el orquestador le pide a cada capacidad, anotado en orden; y el rechazo que el cable contesta cuando
/// <c>falla</c> devuelve un código (lo que ninguna capacidad real sabe hacer a pedido).
/// </summary>
internal sealed class CableQueAnota(IHttpClientFactory real, Func<HttpRequestMessage, string?>? falla) : IHttpClientFactory
{
    private readonly List<(string Metodo, string Ruta, string? Llave, string? Correlacion)> _llamadas = [];

    /// <summary>
    /// Cada petición, con la correlación en curso cuando salió: la que <c>CorrelationHandler</c> pega en la
    /// cabecera de los clientes nombrados (aquí la fábrica es la del arnés y el manejador no está en la cadena).
    /// </summary>
    public IReadOnlyList<(string Metodo, string Ruta, string? Llave, string? Correlacion)> Llamadas { get { lock (_llamadas) return _llamadas.ToList(); } }

    /// <summary>Cuántas veces se pidió <paramref name="metodo"/> a una ruta que casa con <paramref name="patron"/>.</summary>
    public int Veces(string metodo, string patron)
        => Llamadas.Count(l => l.Metodo == metodo && System.Text.RegularExpressions.Regex.IsMatch(l.Ruta, patron));

    public static bool Es(HttpRequestMessage r, string metodo, string patron)
        => r.Method.Method == metodo && System.Text.RegularExpressions.Regex.IsMatch(r.RequestUri!.AbsolutePath, patron);

    public static string? LlaveDe(HttpRequestMessage r)
        => r.Headers.TryGetValues("Idempotency-Key", out var v) ? v.FirstOrDefault() : null;

    public HttpClient CreateClient(string name)
    {
        var interno = real.CreateClient(name);
        return new HttpClient(new Hilo(this, interno)) { BaseAddress = interno.BaseAddress };
    }

    private sealed class Hilo(CableQueAnota cable, HttpClient interno) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            lock (cable._llamadas) cable._llamadas.Add((r.Method.Method, r.RequestUri!.AbsolutePath, LlaveDe(r), Correlation.Current));

            if (cable.Falla(r) is { } codigo)
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

    private string? Falla(HttpRequestMessage r) => falla?.Invoke(r);
}

/// <summary>
/// La renta contra las capacidades reales (línea 005, T007 y T013): lo que queda en Booking, Pricing, Payments y
/// Notifications, y no lo que el orquestador cree haber hecho.
/// </summary>
/// <remarks>
/// La oferta de todos los casos es la de <see cref="RentaDeAlquilerReal.PublicarOferta"/>: dos unidades por siete
/// días caen en el tramo de 30 000, así que el alquiler son 420 000 y la garantía 2 × 250 000 = 500 000. Con la
/// tarifa base (40 000) saldrían 560 000: elegir mal el tramo no pasa en verde.
/// </remarks>
public sealed class RentaDeAlquilerRealTests
{
    private const decimal Alquiler = 420_000m;
    private const decimal Garantia = 500_000m;

    private static string Id(JsonElement cuerpo) => cuerpo.GetProperty("id").GetString()!;

    private static decimal Monto(JsonElement cuerpo, string nombre)
        => cuerpo.GetProperty("datos").GetProperty(nombre).GetProperty("amount").GetDecimal();

    private static string? Texto(JsonElement cuerpo, string nombre)
        => cuerpo.GetProperty("datos").GetProperty(nombre) is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static async Task<RentaDeAlquilerReal> ConOferta(Func<HttpRequestMessage, string?>? falla = null,
        IReadOnlyDictionary<string, string>? ajustes = null, decimal garantia = 250_000m)
    {
        var r = new RentaDeAlquilerReal(falla, ajustes);
        var (estado, oferta) = await r.PublicarOferta(garantia: garantia);
        Assert.True(estado == HttpStatusCode.OK, oferta.ToString());
        return r;
    }

    private static string? GarantiaDe(RentaDeAlquilerReal r, string id)
        => r.Motor.Find(id)!.Datos.GetValueOrDefault("garantiaId")?.Valor is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static string? CobroDe(RentaDeAlquilerReal r, string id)
        => r.Motor.Find(id)!.Datos.GetValueOrDefault("cobroId")?.Valor is { ValueKind: JsonValueKind.String } v ? v.GetString() : null;

    private static async Task<string> EstadoDelCobro(RentaDeAlquilerReal r, string cobro)
        => (await r.Leer("payments", $"v1/payments/{cobro}")).GetProperty("status").GetString()!;

    [Fact]
    public async Task Arranca_y_una_oferta_publicada_se_lee_de_vuelta_en_las_capacidades()
    {
        using var r = new RentaDeAlquilerReal();

        using var salud = await r.Orquestador.GetAsync(new Uri("/health", UriKind.Relative));
        Assert.Equal(HttpStatusCode.OK, salud.StatusCode);

        var (estado, oferta) = await r.PublicarOferta();
        Assert.True(estado == HttpStatusCode.OK, oferta.ToString());

        Assert.Equal(40_000m, await r.Total(("alquiler.tarifa", "andamio-6m@1", 1)));
        Assert.Equal(30_000m, await r.Total(("alquiler.tarifa", "andamio-6m@7", 1)));
        Assert.Equal(250_000m, await r.Total(("alquiler.garantia-unidad", "andamio-6m", 1)));
        Assert.Equal(0, await r.Tomadas());
    }

    [Fact]
    public async Task Dos_unidades_siete_dias_dos_reservas_una_captura_por_el_tramo_y_la_garantia_viva()
    {
        using var r = await ConOferta();

        var (estado, cuerpo) = await r.Rentar();

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(Alquiler, Monto(cuerpo, "total"));
        Assert.Equal(Garantia, Monto(cuerpo, "garantia"));

        var cobro = await r.Leer("payments", $"v1/payments/{Texto(cuerpo, "cobroId")}");
        Assert.Equal("Captured", cobro.GetProperty("status").GetString());
        Assert.Equal(Alquiler, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());

        var garantia = await r.Leer("payments", $"v1/payments/{Texto(cuerpo, "garantiaId")}");
        Assert.Equal("Authorized", garantia.GetProperty("status").GetString());
        Assert.Equal(Garantia, garantia.GetProperty("amount").GetProperty("amount").GetDecimal());

        var reservas = await r.Lista("booking", $"v1/reservations?resourceId={await r.Recurso()}");
        Assert.Equal(2, reservas.Count);
        Assert.All(reservas, x => Assert.Equal("Confirmed", x.GetProperty("status").GetString()));
        Assert.Equal(2, await r.Tomadas());
        Assert.Equal(1, r.Cable.Veces("POST", "^/v1/payments/[^/]+/capture$"));

        var avisos = await r.Lista("notifications", "v1/deliveries?toKind=alquiler.arrendatario&toId=u-1");
        Assert.Single(avisos);
    }

    /// <summary>
    /// El aviso de la renta (T030): UN correo por alquiler confirmado, con la plantilla de
    /// <c>tools/provisionar.plantillas.json</c> —el número, lo cobrado, el enlace y la garantía retenida sin cobrarse—;
    /// ninguno de más al repetir <c>cerrar</c>, y ninguno con el servicio de avisos caído, sin que el alquiler deje de
    /// confirmarse.
    /// </summary>
    [Fact]
    public async Task El_aviso_llega_una_vez_por_alquiler_y_con_el_servicio_caido_el_alquiler_se_confirma_igual()
    {
        using (var r = await ConOferta())
        {
            var (estado, cuerpo) = await r.Rentar();
            Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
            await r.Sobre(Id(cuerpo), "cerrar", contacto: RentaDeAlquilerReal.Contacto());

            var (asunto, html) = Correo(File.ReadAllText(Assert.Single(Directory.GetFiles(r.Buzon, "*.eml"))));
            Assert.Equal($"Tu alquiler {cuerpo.GetProperty("numero").GetString()} está confirmado", asunto);
            Assert.Contains("Hola Ana María", html, StringComparison.Ordinal);
            Assert.Contains("https://sitio.ejemplo/alquiler/mis-alquileres", html, StringComparison.Ordinal);
            Assert.Contains("retenida sin cobrarse", html, StringComparison.Ordinal);
            Assert.Contains("420", html, StringComparison.Ordinal);
        }

        using var caido = await ConOferta(falla: x => CableQueAnota.Es(x, "POST", "^/v1/deliveries$") ? "notifications.unavailable" : null);
        var (confirmado, renta) = await caido.Rentar();

        Assert.True(confirmado == HttpStatusCode.OK, renta.ToString());
        Assert.Equal("Completed", renta.GetProperty("estado").GetString());
        Assert.Empty(Directory.Exists(caido.Buzon) ? Directory.GetFiles(caido.Buzon, "*.eml") : []);
    }

    /// <summary>El asunto (palabra codificada UTF-8) y el cuerpo HTML (Base64) de un <c>.eml</c> de la carpeta de recogida.</summary>
    private static (string Asunto, string Html) Correo(string eml)
    {
        var corte = eml.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var cabeceras = eml[..corte].Replace("\r\n ", " ", StringComparison.Ordinal).Replace("\r\n\t", " ", StringComparison.Ordinal);
        var asunto = cabeceras.Split("\r\n").Single(l => l.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase))["Subject:".Length..].Trim();
        asunto = string.Concat(Regex.Matches(asunto, @"=\?utf-8\?B\?([^?]+)\?=", RegexOptions.IgnoreCase)
            .Select(m => Encoding.UTF8.GetString(Convert.FromBase64String(m.Groups[1].Value))));
        var html = Encoding.UTF8.GetString(Convert.FromBase64String(Regex.Replace(eml[(corte + 4)..], @"\s", string.Empty)));
        return (asunto, html);
    }

    /// <summary>
    /// Cerrado, el alquiler sale con el equipo y las unidades —lo que el contrato del CMS lleva— y queda en la lista
    /// de su dueño: así «mis alquileres» lo encuentra aunque se cerrara la pestaña justo después de pagar (T024, NN-7).
    /// </summary>
    [Fact]
    public async Task Cerrado_sale_con_equipo_y_unidades_y_queda_en_la_lista_de_su_dueno_y_de_nadie_mas()
    {
        using var r = await ConOferta();

        var (estado, cuerpo) = await r.Rentar();

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Equal(RentaDeAlquilerReal.Equipo, Texto(cuerpo, "equipo"));
        Assert.Equal(2, cuerpo.GetProperty("datos").GetProperty("cantidad").GetInt32());

        var (enLista, mios) = await r.Mios();
        Assert.Equal(HttpStatusCode.OK, enLista);
        var item = Assert.Single(mios.GetProperty("items").EnumerateArray());
        Assert.Equal(Id(cuerpo), item.GetProperty("id").GetString());
        Assert.Equal("Completed", item.GetProperty("status").GetString());

        var (_, ajenos) = await r.Mios(RentaDeAlquilerReal.Sujeto("otro"));
        Assert.Empty(ajenos.GetProperty("items").EnumerateArray());
        var (sinSujeto, _) = await r.Mios(sinSujeto: true);
        Assert.Equal(HttpStatusCode.BadRequest, sinSujeto);

        // Abierto y sin cerrar no se anota: todavía no se cobró nada.
        var (abierto, _) = await r.Abrir("alq-2", RentaDeAlquilerReal.Pedido(unidades: 1), RentaDeAlquilerReal.Sujeto("u-3"));
        Assert.Equal(HttpStatusCode.Created, abierto);
        Assert.Empty((await r.Mios(RentaDeAlquilerReal.Sujeto("u-3"))).Cuerpo.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Un_cuerpo_con_total_y_garantia_se_rechaza_sin_tocar_nada()
    {
        using var r = await ConOferta();
        var (inicio, fin) = RentaDeAlquilerReal.Ventana(7);

        var (estado, cuerpo) = await r.Abrir(pedido: new
        {
            equipo = RentaDeAlquilerReal.Equipo,
            unidades = new[] { new { unidad = 1 }, new { unidad = 2 } },
            ventana = new { start = inicio, end = fin },
            total = new { amount = 1m, currency = "COP" },
            garantia = new { amount = 1m, currency = "COP" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, estado);
        Assert.Equal("alquiler.entrada_invalida", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, await r.Tomadas());
        Assert.Equal(0, r.Cable.Veces("POST", "^/v1/payments$"));
    }

    /// <summary>
    /// Un pedido no aparta en la agenda de otro vertical (NR-1 del director, hallada por 008).
    /// </summary>
    /// <remarks>
    /// <para><b>El flujo no toma una <c>ref</c> del cuerpo</b>: <c>equipo</c> es un texto, y el recurso se busca con
    /// la clase fija <c>alquiler.equipo</c> (<c>RentalSubject.For</c>) sólo si el equipo tiene oferta publicada. Así
    /// que la agenda que comparten todos los verticales no se alcanza desde aquí: ni nombrando el id de un recurso
    /// ajeno, ni mandando una referencia con su clase.</para>
    /// </remarks>
    [Fact]
    public async Task Un_pedido_no_aparta_en_la_agenda_de_otro_vertical()
    {
        using var r = await ConOferta();
        using (var booking = r.Capacidades.CreateClient("booking"))
        using (var peticion = new HttpRequestMessage(HttpMethod.Post, "v1/resources")
        {
            Content = JsonContent.Create(new { subjectKind = "salud.profesional", subjectId = "dra-1", capacity = 1, timeZoneId = "UTC" }),
        })
        {
            peticion.Headers.Add("Idempotency-Key", "recurso-ajeno");
            using var creado = await booking.SendAsync(peticion);
            Assert.Equal(HttpStatusCode.Created, creado.StatusCode);
        }
        var (inicio, fin) = RentaDeAlquilerReal.Ventana(7);
        var antes = r.Cable.Llamadas.Count;

        var (porId, rechazoPorId) = await r.Abrir("alq-1", new
        {
            equipo = "dra-1", unidades = new[] { new { unidad = 1 } }, ventana = new { start = inicio, end = fin },
        });
        var (porRef, rechazoPorRef) = await r.Abrir("alq-2", new
        {
            equipo = new { kind = "salud.profesional", id = "dra-1" }, unidades = new[] { new { unidad = 1 } }, ventana = new { start = inicio, end = fin },
        });

        Assert.Equal(HttpStatusCode.NotFound, porId);
        Assert.Equal("alquiler.equipment_not_offered", rechazoPorId.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, porRef);
        Assert.Equal("alquiler.entrada_invalida", rechazoPorRef.GetProperty("code").GetString());
        Assert.Empty(r.Cable.Llamadas.Skip(antes));
    }

    [Fact]
    public async Task Sin_unidades_suficientes_se_rechaza_y_suelta_lo_que_aparto()
    {
        using var r = await ConOferta();
        Assert.Equal(HttpStatusCode.OK, (await r.Rentar("alq-1")).Estado);

        var (estado, cuerpo) = await r.Abrir("alq-2");

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.at_capacity", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-2")!.Status);
        Assert.Equal(2, await r.Tomadas());
    }

    [Fact]
    public async Task Garantia_negada_suelta_las_unidades_y_no_autoriza_el_alquiler()
    {
        using var r = await ConOferta(falla: x => CableQueAnota.Es(x, "POST", "^/v1/payments$")
            && CableQueAnota.LlaveDe(x)!.EndsWith("|deposit", StringComparison.Ordinal) ? "payments.payment_declined" : null);

        var (estado, cuerpo) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.payment_declined", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-1")!.Status);
        Assert.Equal(0, await r.Tomadas());
        Assert.DoesNotContain(r.Cable.Llamadas, l => l.Llave?.EndsWith("|rental", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Alquiler_negado_anula_la_garantia_y_suelta_las_unidades()
    {
        using var r = await ConOferta(falla: x => CableQueAnota.Es(x, "POST", "^/v1/payments$")
            && CableQueAnota.LlaveDe(x)!.EndsWith("|rental", StringComparison.Ordinal) ? "payments.payment_declined" : null);

        var (estado, _) = await r.Abrir();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-1")!.Status);
        Assert.Equal("Voided", await EstadoDelCobro(r, GarantiaDe(r, "alq-1")!));
        Assert.Equal(0, await r.Tomadas());
    }

    [Fact]
    public async Task Captura_negada_anula_los_dos_cobros_y_suelta_las_unidades()
    {
        using var r = await ConOferta(falla: x => CableQueAnota.Es(x, "POST", "^/v1/payments/[^/]+/capture$") ? "payments.capture_failed" : null);

        var (estado, cuerpo) = await r.Rentar();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("payments.capture_failed", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-1")!.Status);
        Assert.Equal("Voided", await EstadoDelCobro(r, CobroDe(r, "alq-1")!));
        Assert.Equal("Voided", await EstadoDelCobro(r, GarantiaDe(r, "alq-1")!));
        Assert.Equal(0, await r.Tomadas());
    }

    [Fact]
    public async Task Una_unidad_que_no_se_confirma_tras_cobrar_devuelve_el_alquiler_entero_anula_la_garantia_y_cancela()
    {
        var confirmaciones = 0;
        using var r = await ConOferta(falla: x => CableQueAnota.Es(x, "POST", "^/v1/holds/[^/]+/confirm$")
            && Interlocked.Increment(ref confirmaciones) == 2 ? "booking.hold_expired" : null);

        var (estado, cuerpo) = await r.Rentar();

        Assert.Equal(HttpStatusCode.Conflict, estado);
        Assert.Equal("booking.hold_expired", cuerpo.GetProperty("code").GetString());
        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-1")!.Status);

        var cobro = await r.Leer("payments", $"v1/payments/{CobroDe(r, "alq-1")}");
        Assert.Equal(0m, cobro.GetProperty("refundable").GetProperty("amount").GetDecimal());
        Assert.Equal("Voided", await EstadoDelCobro(r, GarantiaDe(r, "alq-1")!));

        var reservas = await r.Lista("booking", $"v1/reservations?resourceId={await r.Recurso()}");
        Assert.Equal("Cancelled", Assert.Single(reservas).GetProperty("status").GetString());
        Assert.Equal(0, await r.Tomadas());
    }

    [Fact]
    public async Task Cerrar_dos_veces_captura_una_y_un_alquiler_ajeno_no_existe()
    {
        using var r = await ConOferta();
        var (_, abierto) = await r.Abrir();
        var id = Id(abierto);

        var (ajeno, rechazo) = await r.Sobre(id, null, sujeto: RentaDeAlquilerReal.Sujeto("u-2"));
        Assert.Equal(HttpStatusCode.NotFound, ajeno);
        Assert.Equal("alquiler.saga_not_found", rechazo.GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, (await r.Sobre(id, "cerrar", sujeto: RentaDeAlquilerReal.Sujeto("u-2"))).Estado);

        Assert.Equal(HttpStatusCode.OK, (await r.Sobre(id, "cerrar", contacto: RentaDeAlquilerReal.Contacto())).Estado);
        var (otraVez, cuerpo) = await r.Sobre(id, "cerrar", contacto: RentaDeAlquilerReal.Contacto());

        Assert.Equal(HttpStatusCode.OK, otraVez);
        Assert.Equal("Completed", cuerpo.GetProperty("estado").GetString());
        Assert.Equal(1, r.Cable.Veces("POST", "^/v1/payments/[^/]+/capture$"));
    }

    [Fact]
    public async Task Tras_deshacer_la_misma_llave_abre_otro_alquiler()
    {
        var negar = true;
        using var r = await ConOferta(falla: x => negar && CableQueAnota.Es(x, "POST", "^/v1/payments$") ? "payments.payment_declined" : null);
        Assert.Equal(HttpStatusCode.Conflict, (await r.Abrir()).Estado);

        negar = false;
        var (estado, cuerpo) = await r.Rentar();

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.NotEqual("alq-1", Id(cuerpo));
        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-1")!.Status);
        Assert.Equal(2, await r.Tomadas());
    }

    [Fact]
    public async Task Abandonada_sin_confirmar_el_barrido_suelta_las_unidades_y_anula_los_dos_cobros()
    {
        using var r = await ConOferta();
        Assert.Equal(HttpStatusCode.Created, (await r.Abrir()).Estado);
        Assert.Equal(2, await r.Tomadas());

        r.Reloj.Avanzar(TimeSpan.FromMinutes(16));
        await r.UnaVuelta();

        Assert.Equal(SagaStatus.Compensated, r.Motor.Find("alq-1")!.Status);
        Assert.Equal(0, await r.Tomadas());
        Assert.Equal("Voided", await EstadoDelCobro(r, CobroDe(r, "alq-1")!));
        Assert.Equal("Voided", await EstadoDelCobro(r, GarantiaDe(r, "alq-1")!));
    }

    [Fact]
    public async Task Confirmada_y_treinta_dias_despues_el_barrido_no_la_toca()
    {
        using var r = await ConOferta();
        var (_, cuerpo) = await r.Rentar();

        r.Reloj.Avanzar(TimeSpan.FromDays(30));
        await r.UnaVuelta();

        Assert.Equal(SagaStatus.Completed, r.Motor.Find(Id(cuerpo))!.Status);
        Assert.Equal("Authorized", await EstadoDelCobro(r, Texto(cuerpo, "garantiaId")!));
        Assert.Equal(2, await r.Tomadas());
    }

    [Fact]
    public async Task Cotizar_no_aparta_ni_autoriza_y_su_total_es_el_que_se_cobra()
    {
        using var r = await ConOferta();

        var (estado, cotizado) = await r.Cotizar();

        Assert.True(estado == HttpStatusCode.OK, cotizado.ToString());
        Assert.Equal(Alquiler, Monto(cotizado, "total"));
        Assert.Equal(Garantia, Monto(cotizado, "garantia"));
        Assert.Equal(0, await r.Tomadas());
        Assert.Equal(0, r.Cable.Veces("POST", "^/v1/payments$"));
        Assert.Equal(0, r.Cable.Veces("POST", "^/v1/holds$"));

        var (_, rentado) = await r.Rentar();
        var cobro = await r.Leer("payments", $"v1/payments/{Texto(rentado, "cobroId")}");
        var garantia = await r.Leer("payments", $"v1/payments/{Texto(rentado, "garantiaId")}");
        Assert.Equal(Monto(cotizado, "total"), cobro.GetProperty("amount").GetProperty("amount").GetDecimal());
        Assert.Equal(Monto(cotizado, "garantia"), garantia.GetProperty("amount").GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task Republicar_la_oferta_no_cambia_lo_confirmado_y_lo_nuevo_cotiza_con_el_valor_nuevo()
    {
        using var r = await ConOferta();
        var (_, rentado) = await r.Rentar();

        Assert.Equal(HttpStatusCode.OK, (await r.PublicarOferta(desdeSiete: 35_000m, llave: "oferta-2")).Estado);

        var (_, consultado) = await r.Sobre(Id(rentado), null);
        Assert.Equal(Alquiler, Monto(consultado, "total"));
        var cobro = await r.Leer("payments", $"v1/payments/{Texto(rentado, "cobroId")}");
        Assert.Equal(Alquiler, cobro.GetProperty("amount").GetProperty("amount").GetDecimal());

        var (_, nuevo) = await r.Cotizar();
        Assert.Equal(35_000m * 7 * 2, Monto(nuevo, "total"));
    }

    [Fact]
    public async Task Un_equipo_sin_garantia_se_alquila_sin_retener_nada()
    {
        using var r = await ConOferta(garantia: 0m);

        var (estado, cuerpo) = await r.Rentar();

        Assert.True(estado == HttpStatusCode.OK, cuerpo.ToString());
        Assert.Null(Texto(cuerpo, "garantiaId"));
        Assert.Equal(1, r.Cable.Veces("POST", "^/v1/payments$"));
    }

    /// <summary>
    /// Cada petición se sigue por UNA correlación hasta las capacidades, el registro nombra el alquiler, y ningún
    /// registro lleva el correo ni el nombre del contacto (T014, FR-036).
    /// </summary>
    /// <remarks>
    /// Lo que N1 registra por petición es la línea de la correlación (con la ruta, que nombra el alquiler); una
    /// línea POR PASO con el número no la da el intérprete hoy (NN-18 de la línea 005).
    /// </remarks>
    [Fact]
    public async Task Cada_peticion_se_sigue_por_su_correlacion_y_el_registro_no_lleva_el_contacto()
    {
        using var r = await ConOferta();

        var antes = r.Cable.Llamadas.Count;
        var (abrio, abierto) = await r.Abrir(correlacion: "rentaabrir01");
        var entre = r.Cable.Llamadas.Count;
        var (cerro, _) = await r.Sobre(Id(abierto), "cerrar", contacto: RentaDeAlquilerReal.Contacto(), correlacion: "rentacerrar01");

        Assert.Equal(HttpStatusCode.Created, abrio);
        Assert.Equal(HttpStatusCode.OK, cerro);
        var deAbrir = r.Cable.Llamadas.Skip(antes).Take(entre - antes).ToList();
        var deCerrar = r.Cable.Llamadas.Skip(entre).ToList();
        Assert.Contains(deAbrir, l => l.Ruta == "/v1/holds");
        Assert.Contains(deCerrar, l => l.Ruta == "/v1/deliveries");
        Assert.All(deAbrir, l => Assert.Equal("rentaabrir01", l.Correlacion));
        Assert.All(deCerrar, l => Assert.Equal("rentacerrar01", l.Correlacion));

        Assert.Contains(r.Log.Lineas, l => l.Contains("/v1/flujos/alquiler.renta/alq-1/cerrar", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lineas, l => l.Contains("ana@ejemplo.co", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(r.Log.Lineas, l => l.Contains("Ana María", StringComparison.Ordinal));
        Assert.DoesNotContain(r.Log.Lineas, l => l.Contains(RentaDeAlquilerReal.Contacto(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_compensacion_que_se_rinde_avisa_a_la_guardia_una_vez()
    {
        using var r = await ConOferta(
            falla: x => CableQueAnota.Es(x, "POST", "^/v1/payments$") && CableQueAnota.LlaveDe(x)!.EndsWith("|rental", StringComparison.Ordinal)
                ? "payments.payment_declined"
                : CableQueAnota.Es(x, "POST", "^/v1/payments/[^/]+/void$") ? "payments.unavailable" : null,
            ajustes: new Dictionary<string, string>
            {
                ["Alquiler:Alerts:ToKind"] = "alquiler.guardia",
                ["Alquiler:Alerts:ToId"] = "operaciones",
                ["Alquiler:Alerts:Address"] = "guardia@ejemplo.co",
            });

        Assert.Equal(HttpStatusCode.Conflict, (await r.Abrir()).Estado);
        for (var vuelta = 0; vuelta < 12; vuelta++)
        {
            r.Reloj.Avanzar(TimeSpan.FromHours(6));
            await r.UnaVuelta();
        }

        Assert.Equal(SagaStatus.CompensationFailed, r.Motor.Find("alq-1")!.Status);
        var avisos = await r.Lista("notifications", "v1/deliveries?toKind=alquiler.guardia&toId=operaciones");
        Assert.Single(avisos);
    }
}
