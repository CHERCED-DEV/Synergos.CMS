using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Comprando un carrito de viaje contra <c>Bff.Viajes</c> (HU #40, rebanada 3).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba no es «comprar». Es lo que este camino tiene de distinto del motor en
/// proceso, que son tres cosas y ninguna se ve desde el resultado feliz:</para>
///
/// <list type="bullet">
///   <item>que se pida confirmación <b>PARCIAL</b> — sin eso, un auto agotado tumbaría el vuelo;</item>
///   <item>que lo no cumplido se <b>DEVUELVA</b>, con el monto que sólo el CMS conoce;</item>
///   <item>que una <b>caída</b> no se confunda con un rechazo: de la primera no sabemos si el
///         viaje quedó hecho, y decir «cancelado» sería inventarlo.</item>
/// </list>
/// </remarks>
public sealed class HttpTravelCartEngineTests
{
    private sealed class OrquestadorFalso : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpResponseMessage>> _rutas = new(StringComparer.OrdinalIgnoreCase);

        public List<(string Method, string Path, string? Key, string? Body)> Llamadas { get; } = new();
        public HashSet<string> Caidas { get; } = new(StringComparer.OrdinalIgnoreCase);

        public OrquestadorFalso Ok(string ruta, string json)
        {
            _rutas[ruta] = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            return this;
        }

        public OrquestadorFalso Falla(string ruta, HttpStatusCode codigo, string code, string detail)
        {
            _rutas[ruta] = () => new HttpResponseMessage(codigo)
            {
                Content = new StringContent(
                    $$"""{"title":"x","status":{{(int)codigo}},"detail":"{{detail}}","code":"{{code}}"}""",
                    Encoding.UTF8, "application/problem+json"),
            };
            return this;
        }

        public OrquestadorFalso Caida(string ruta) { Caidas.Add(ruta); return this; }

        public int Veces(string sufijo)
            => Llamadas.Count(l => l.Path.EndsWith(sufijo, StringComparison.Ordinal));

        public (string Method, string Path, string? Key, string? Body) Ultima(string sufijo)
            => Llamadas.Last(l => l.Path.EndsWith(sufijo, StringComparison.Ordinal));

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var clave = $"{req.Method.Method} {path}";
            Llamadas.Add((req.Method.Method, path,
                req.Headers.TryGetValues("Idempotency-Key", out var k) ? k.FirstOrDefault() : null,
                req.Content?.ReadAsStringAsync(ct).GetAwaiter().GetResult()));

            if (Caidas.Contains(clave)) throw new HttpRequestException("guionado: caída");

            return Task.FromResult(_rutas.TryGetValue(clave, out var f)
                ? f()
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class FabricaFalsa : IHttpClientFactory
    {
        private readonly OrquestadorFalso _h;
        public FabricaFalsa(OrquestadorFalso h) => _h = h;
        public HttpClient CreateClient(string name)
            => new(_h, disposeHandler: false) { BaseAddress = new Uri("http://viajes.local/") };
    }

    private sealed class Monitor<T> : IOptionsMonitor<T>
    {
        public Monitor(T v) => CurrentValue = v;
        public T CurrentValue { get; }
        public T Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<T, string?> l) => null;
    }

    private static DateTimeOffset F(int dia, int hora) => new(2026, 9, dia, hora, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<TravelCartItem> Carrito = new[]
    {
        new TravelCartItem(TravelProductType.Flight, "AV-8020/Y", "Vuelo BOG→MDE", 400000m, "COP", F(10, 8), F(10, 9)),
        new TravelCartItem(TravelProductType.Hotel, "DBL/FLEX", "Hotel 2 noches", 350000m, "COP", F(10, 0), F(12, 0)),
        new TravelCartItem(TravelProductType.Car, "ECAR", "Auto compacto", 150000m, "COP", F(10, 12), F(12, 12)),
    };

    private static readonly TravelGuest Viajero = new("Ana Pérez", "Ana@Ejemplo.co");

    private static readonly IReadOnlyList<TravelCartSettledLine> Lineas = Carrito
        .Select(i => new TravelCartSettledLine(i.OfferId, string.Empty, i.Price, i.Currency))
        .ToList();

    private const string ViajeApartado = """
        {"id":"tp-1","travellerKind":"viajes.viajero","travellerId":"abc","status":"Running",
         "total":{"amount":900000,"currency":"COP"},"pendingCompensations":0,"lastError":null,
         "items":[
           {"productRef":"AV-8020/Y","productLabel":"Vuelo","start":"2026-09-10T08:00:00+00:00","end":"2026-09-10T09:00:00+00:00","confirmed":false},
           {"productRef":"DBL/FLEX","productLabel":"Hotel","start":"2026-09-10T00:00:00+00:00","end":"2026-09-12T00:00:00+00:00","confirmed":false},
           {"productRef":"ECAR","productLabel":"Auto","start":"2026-09-10T12:00:00+00:00","end":"2026-09-12T12:00:00+00:00","confirmed":false}]}
        """;

    /// <summary>Todo confirmado.</summary>
    private const string ViajeCompleto = """
        {"id":"tp-1","travellerKind":"viajes.viajero","travellerId":"abc","status":"Completed",
         "total":{"amount":900000,"currency":"COP"},"pendingCompensations":0,"lastError":null,
         "items":[
           {"productRef":"AV-8020/Y","productLabel":"Vuelo","start":"2026-09-10T08:00:00+00:00","end":"2026-09-10T09:00:00+00:00","confirmed":true},
           {"productRef":"DBL/FLEX","productLabel":"Hotel","start":"2026-09-10T00:00:00+00:00","end":"2026-09-12T00:00:00+00:00","confirmed":true},
           {"productRef":"ECAR","productLabel":"Auto","start":"2026-09-10T12:00:00+00:00","end":"2026-09-12T12:00:00+00:00","confirmed":true}]}
        """;

    /// <summary>El auto se cayó; el resto salió.</summary>
    private const string ViajeParcial = """
        {"id":"tp-1","travellerKind":"viajes.viajero","travellerId":"abc","status":"Completed",
         "total":{"amount":900000,"currency":"COP"},"pendingCompensations":0,"lastError":null,
         "items":[
           {"productRef":"AV-8020/Y","productLabel":"Vuelo","start":"2026-09-10T08:00:00+00:00","end":"2026-09-10T09:00:00+00:00","confirmed":true},
           {"productRef":"DBL/FLEX","productLabel":"Hotel","start":"2026-09-10T00:00:00+00:00","end":"2026-09-12T00:00:00+00:00","confirmed":true},
           {"productRef":"ECAR","productLabel":"Auto","start":"2026-09-10T12:00:00+00:00","end":"2026-09-12T12:00:00+00:00","confirmed":false,"unfulfilled":true}]}
        """;

    private const string ViajeCancelado = """
        {"id":"tp-1","travellerKind":"viajes.viajero","travellerId":"abc","status":"Compensated",
         "total":{"amount":900000,"currency":"COP"},"pendingCompensations":0,"lastError":null,
         "refunded":{"amount":900000,"currency":"COP"},"items":[]}
        """;

    private static OrquestadorFalso Feliz() => new OrquestadorFalso()
        .Ok("POST /v1/trips", ViajeApartado)
        .Ok("POST /v1/trips/tp-1/confirm", ViajeCompleto)
        .Ok("POST /v1/trips/tp-1/refund", ViajeParcial)
        .Ok("POST /v1/trips/tp-1/cancel", ViajeCancelado);

    private static (HttpTravelCartEngine Motor, OrquestadorFalso Orq) Nuevo(OrquestadorFalso? orq = null)
    {
        var o = orq ?? Feliz();
        return (new HttpTravelCartEngine(
            new FabricaFalsa(o),
            new Monitor<ViajesSettings>(new ViajesSettings { Mode = "Bff" }),
            NullLogger<HttpTravelCartEngine>.Instance), o);
    }

    // ── Apartar ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Se aparta pidiendo confirmación PARCIAL, y el periodo de cada ítem viaja tal cual.
    /// </summary>
    /// <remarks>
    /// Lo parcial es la decisión de este motor: quien compró un vuelo, un hotel y un auto no
    /// pierde el vuelo porque el auto se agotó. Es la política que ya tenía el motor en proceso, y
    /// mantenerla es lo que hace que cambiar de motor no cambie lo que le pasa a quien compra.
    /// </remarks>
    [Fact]
    public async Task Aparta_pidiendo_confirmacion_parcial()
    {
        var (motor, orq) = Nuevo();

        var apartado = await motor.HoldAllAsync(Carrito, Viajero, "trip_abc123", CancellationToken.None);

        var pedido = orq.Ultima("/v1/trips");
        Assert.Contains("\"partialConfirm\":true", pedido.Body, StringComparison.Ordinal);
        Assert.Equal("trip_abc123", pedido.Key);

        // Cada ítem con su producto y su ventana. El productRef es el OfferId tal cual: la vía
        // hotel usa ese mismo string, y prefijarlo haría de la misma habitación dos recursos.
        Assert.Contains("\"productRef\":\"AV-8020/Y\"", pedido.Body, StringComparison.Ordinal);
        Assert.Contains("\"productRef\":\"DBL/FLEX\"", pedido.Body, StringComparison.Ordinal);
        Assert.Contains("2026-09-12T12:00:00+00:00", pedido.Body, StringComparison.Ordinal);

        // El total lo dice el orquestador, que cotizó. Acá suma lo mismo, pero se toma el suyo.
        Assert.Equal(900000m, apartado.Total);
        Assert.Equal("tp-1", apartado.EngineRef);
        Assert.Equal(3, apartado.Items.Count);
    }

    /// <summary>El correo del viajero NO cruza: cruza un seudónimo estable.</summary>
    /// <remarks>
    /// Mandarlo en crudo lo dejaría escrito en el disco de otro servicio. Lo destapó la
    /// verificación en vivo de la HU #35 y volvió a costar un defecto en la tienda (#47).
    /// </remarks>
    [Fact]
    public async Task El_viajero_cruza_seudonimizado()
    {
        var (motor, orq) = Nuevo();

        await motor.HoldAllAsync(Carrito, Viajero, "trip_abc123", CancellationToken.None);

        var cuerpo = orq.Ultima("/v1/trips").Body!;
        Assert.DoesNotContain("Ejemplo.co", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ana", cuerpo, StringComparison.Ordinal);
    }

    // ── Liquidar ────────────────────────────────────────────────────────────

    /// <summary>Todo confirmado: nada que devolver.</summary>
    [Fact]
    public async Task Un_viaje_entero_no_devuelve_nada()
    {
        var (motor, orq) = Nuevo();

        var liquidado = await motor.SettleAsync("tp-1", "tp-1", Lineas, CancellationToken.None);

        Assert.Equal(0m, liquidado.UnfulfilledAmount);
        Assert.All(liquidado.Items, i => Assert.Equal("Confirmed", i.Status));
        Assert.Equal(0, orq.Veces("/refund"));
    }

    /// <summary>
    /// El ítem que no se cumplió se marca <b>y se devuelve</b>, con el monto que sólo el CMS sabe.
    /// </summary>
    /// <remarks>
    /// Es la mitad que hace que la confirmación parcial no sea un retroceso: sin la devolución, el
    /// auto se suelta, quien compró se queda sin él <i>y</i> sin sus 150.000 — y nada falla.
    /// </remarks>
    [Fact]
    public async Task Lo_no_cumplido_se_marca_y_se_devuelve()
    {
        var orq = Feliz().Ok("POST /v1/trips/tp-1/confirm", ViajeParcial);
        var (motor, _) = Nuevo(orq);

        var liquidado = await motor.SettleAsync("tp-1", "tp-1", Lineas, CancellationToken.None);

        Assert.Equal(150000m, liquidado.UnfulfilledAmount);
        Assert.Equal("Cancelled", liquidado.Items.Single(i => i.OfferId == "ECAR").Status);
        Assert.Equal("Confirmed", liquidado.Items.Single(i => i.OfferId == "AV-8020/Y").Status);

        // Y la orden de devolución sale, por ese monto exacto y en la moneda de la línea.
        Assert.Equal(1, orq.Veces("/refund"));
        var devolucion = orq.Ultima("/refund");
        Assert.Contains("150000", devolucion.Body, StringComparison.Ordinal);
        Assert.Contains("COP", devolucion.Body, StringComparison.Ordinal);
        Assert.False(string.IsNullOrWhiteSpace(devolucion.Key), "Devolver es relativo: exige llave.");
    }

    /// <summary>
    /// Si la devolución falla, el viaje sigue confirmado.
    /// </summary>
    /// <remarks>
    /// Lo que SÍ salió ya está reservado y cobrado. Tumbar la compra porque no se pudo devolver la
    /// parte caída le quitaría a quien compró lo que sí recibió — y no lo arreglaría. Queda dicho
    /// en el log, que es donde una persona lo puede ver.
    /// </remarks>
    [Fact]
    public async Task Una_devolucion_caida_no_tumba_el_viaje()
    {
        var orq = Feliz().Ok("POST /v1/trips/tp-1/confirm", ViajeParcial);
        orq.Falla("POST /v1/trips/tp-1/refund", HttpStatusCode.ServiceUnavailable,
            "payments.provider_down", "El proveedor no responde.");
        var (motor, _) = Nuevo(orq);

        var liquidado = await motor.SettleAsync("tp-1", "tp-1", Lineas, CancellationToken.None);

        Assert.Equal(150000m, liquidado.UnfulfilledAmount);
        Assert.Equal("Confirmed", liquidado.Items.Single(i => i.OfferId == "DBL/FLEX").Status);
    }

    /// <summary>
    /// Un viaje RECHAZADO al confirmar es «nada cumplido», y la saga ya devolvió lo suyo.
    /// </summary>
    [Fact]
    public async Task Un_viaje_rechazado_al_confirmar_es_nada_cumplido()
    {
        var orq = Feliz();
        orq.Falla("POST /v1/trips/tp-1/confirm", HttpStatusCode.Conflict,
            "viajes.nothing_fulfilled", "Ningún ítem del viaje se pudo confirmar.");
        var (motor, _) = Nuevo(orq);

        var liquidado = await motor.SettleAsync("tp-1", "tp-1", Lineas, CancellationToken.None);

        Assert.Equal(900000m, liquidado.UnfulfilledAmount);
        Assert.All(liquidado.Items, i => Assert.Equal("Cancelled", i.Status));
    }

    /// <summary>
    /// Una CAÍDA al confirmar no dice que se canceló.
    /// </summary>
    /// <remarks>
    /// <b>Es la distinción que importa de este método.</b> Un rechazo significa «no se hizo, y ya
    /// se deshizo»; una caída significa «no sé». Devolver «todo cancelado» ante una caída sellaría
    /// la orden como cancelada mientras el orquestador quizá acaba de confirmar el viaje entero, y
    /// entonces habría un viaje reservado que este lado da por muerto.
    /// </remarks>
    [Fact]
    public async Task Una_caida_al_confirmar_no_dice_que_se_cancelo()
    {
        var orq = Feliz().Caida("POST /v1/trips/tp-1/confirm");
        var (motor, _) = Nuevo(orq);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => motor.SettleAsync("tp-1", "tp-1", Lineas, CancellationToken.None));
    }

    /// <summary>Un 200 que no quedó Completed tampoco es un viaje confirmado.</summary>
    [Fact]
    public async Task Un_viaje_que_no_quedo_Completed_no_se_da_por_bueno()
    {
        var orq = Feliz().Ok("POST /v1/trips/tp-1/confirm", ViajeApartado);
        var (motor, _) = Nuevo(orq);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => motor.SettleAsync("tp-1", "tp-1", Lineas, CancellationToken.None));
    }

    // ── Soltar ──────────────────────────────────────────────────────────────

    /// <summary>Cuánto se devolvió lo dice el orquestador, no se deduce del total.</summary>
    /// <remarks>
    /// Deducirlo sería adivinar: puede haberse devuelto algo antes por un ítem no cumplido, y este
    /// lado no puede preguntarle a <c>Api.Payments</c>.
    /// </remarks>
    [Fact]
    public async Task Soltar_devuelve_lo_que_dice_el_orquestador()
    {
        var (motor, orq) = Nuevo();

        var soltado = await motor.ReleaseAsync("tp-1", "tp-1", Lineas, "el viajero canceló", CancellationToken.None);

        Assert.True(soltado.Refunded);
        Assert.Equal(900000m, soltado.Amount);
        Assert.Equal(1, orq.Veces("/cancel"));
    }

    /// <summary>Un viaje que nunca se cobró se suelta sin devolver nada.</summary>
    [Fact]
    public async Task Soltar_sin_cobro_no_devuelve_nada()
    {
        var orq = Feliz().Ok("POST /v1/trips/tp-1/cancel",
            ViajeCancelado.Replace("""\"refunded\":{\"amount\":900000,\"currency\":\"COP\"},""", string.Empty)
                .Replace("\"refunded\":{\"amount\":900000,\"currency\":\"COP\"},", string.Empty));
        var (motor, _) = Nuevo(orq);

        var soltado = await motor.ReleaseAsync("tp-1", "tp-1", Lineas, "el viajero canceló", CancellationToken.None);

        Assert.False(soltado.Refunded);
        Assert.Equal(0m, soltado.Amount);
    }

    /// <summary>
    /// Una orden apartada ANTES de que se guardara la referencia del motor se sigue pudiendo cerrar.
    /// </summary>
    /// <remarks>
    /// El respaldo es la sesión de pago, que en este motor es el mismo identificador. Sin él,
    /// cambiar de versión dejaría carritos a medias sin forma de confirmarlos ni cancelarlos.
    /// </remarks>
    [Fact]
    public async Task Una_orden_sin_referencia_de_motor_se_cierra_igual()
    {
        var (motor, _) = Nuevo();

        var liquidado = await motor.SettleAsync(null, "tp-1", Lineas, CancellationToken.None);

        Assert.Equal(0m, liquidado.UnfulfilledAmount);
    }
}
