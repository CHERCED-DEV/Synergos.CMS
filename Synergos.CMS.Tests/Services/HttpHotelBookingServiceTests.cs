using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Reservando un hotel de verdad contra <c>Bff.Viajes</c> (HU #36, rebanada 2b).
/// </summary>
/// <remarks>
/// <para>Lo que se prueba no es «reservar». Es lo que este camino tiene de distinto: <b>los
/// sustantivos de hotel se quedan de este lado</b> y el orquestador solo ve un producto opaco y
/// una ventana; y <b>el precio lo pone la capacidad</b>, no el buscador.</para>
///
/// <list type="bullet">
///   <item>que el CMS anote la habitación, la tarifa y el huésped de su lado;</item>
///   <item>que el total que quede sea el COTIZADO y no el que traía la petición;</item>
///   <item>que un apartado vencido sea un conflicto y no un cobro;</item>
///   <item>que la penalidad se calcule acá y viaje ya calculada;</item>
///   <item>que cancelar dos veces no devuelva dos veces.</item>
/// </list>
/// </remarks>
public sealed class HttpHotelBookingServiceTests
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

    private const string ViajeCorriendo = """
        {"id":"tp-1","travellerKind":"viajes.viajero","travellerId":"abc","status":"Running",
         "total":{"amount":900000,"currency":"COP"},"pendingCompensations":0,"lastError":null}
        """;

    private static readonly string ViajeCompleto = ViajeCorriendo.Replace("\"Running\"", "\"Completed\"");
    private static readonly string ViajeCompensado = ViajeCorriendo.Replace("\"Running\"", "\"Compensated\"");

    private static OrquestadorFalso Feliz() => new OrquestadorFalso()
        .Ok("POST /v1/trips", ViajeCorriendo)
        .Ok("POST /v1/trips/tp-1/confirm", ViajeCompleto)
        .Ok("POST /v1/trips/tp-1/cancel", ViajeCompensado);

    private static readonly DateOnly Entrada = new(2026, 9, 1);
    private static readonly DateOnly Salida = new(2026, 9, 3);

    /// <summary>Reembolsable con penalidad de una noche — la misma forma que usa el motor propio.</summary>
    private static ICancellationPolicyEvaluator Politica(bool refundable = true, decimal penalidad = 150000m)
    {
        var p = Substitute.For<ICancellationPolicyEvaluator>();
        p.Evaluate(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new CancellationOutcome(refundable, penalidad, "Penalidad de 1 noche."));
        return p;
    }

    private static (HttpHotelBookingService Svc, OrquestadorFalso Orq) Nuevo(
        OrquestadorFalso? orq = null, ICancellationPolicyEvaluator? politica = null)
    {
        var o = orq ?? Feliz();
        return (new HttpHotelBookingService(
            new FabricaFalsa(o),
            new Monitor<ViajesSettings>(new ViajesSettings { Mode = "Bff" }),
            politica ?? Politica(),
            new InMemoryJsonEntityStore(),
            NullLogger<HttpHotelBookingService>.Instance), o);
    }

    /// <summary>El precio que trae la petición es 999.999 A PROPÓSITO: nunca debe salir.</summary>
    private static ReservationRequest Peticion() => new(
        RoomTypeCode: "DBL",
        RatePlanCode: "FLEX",
        CheckIn: Entrada,
        CheckOut: Salida,
        Rooms: new[] { new RoomOccupancy(2, null) },
        GuestName: "Ana Huésped",
        GuestEmail: "ana@ejemplo.co",
        TotalPrice: 999_999m,
        Currency: "COP");

    // ── Apartar ─────────────────────────────────────────────────────────────

    [Fact] // los sustantivos de hotel se quedan de este lado; allá va un producto opaco y una ventana.
    public async Task Apartar_anota_la_habitacion_de_este_lado_y_manda_un_producto_opaco()
    {
        var (svc, orq) = Nuevo();

        var reserva = await svc.HoldAsync(Peticion());

        Assert.Equal("tp-1", reserva.Id);
        Assert.Equal(ReservationStatus.Held, reserva.Status);
        Assert.Equal("DBL", reserva.RoomTypeCode);
        Assert.Equal("FLEX", reserva.RatePlanCode);
        Assert.Equal("Ana Huésped", reserva.GuestName);

        var cuerpo = orq.Llamadas.Single(l => l.Path.EndsWith("/v1/trips", StringComparison.Ordinal)).Body!;
        Assert.Contains("DBL/FLEX", cuerpo, StringComparison.Ordinal);
        foreach (var sustantivo in new[] { "roomType", "ratePlan", "guestName", "Ana Huésped", "ana@ejemplo.co" })
        {
            Assert.DoesNotContain(sustantivo, cuerpo, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// El total que queda es el COTIZADO, no el que traía la petición.
    /// </summary>
    /// <remarks>
    /// Es la mitad del punto de cotizar en la capacidad: si el total lo pusiera el llamador,
    /// cualquiera reservaría la suite al precio de la estándar cambiando un número.
    /// </remarks>
    [Fact]
    public async Task El_precio_de_la_peticion_NO_viaja_y_NO_manda()
    {
        var (svc, orq) = Nuevo();

        var reserva = await svc.HoldAsync(Peticion());

        Assert.Equal(900000m, reserva.TotalPrice);
        var cuerpo = orq.Llamadas.Single(l => l.Path.EndsWith("/v1/trips", StringComparison.Ordinal)).Body!;
        Assert.DoesNotContain("999999", cuerpo, StringComparison.Ordinal);
    }

    [Fact] // la llave sale de lo que se reserva: dos veces lo mismo es un reintento.
    public void La_llave_sale_de_lo_que_se_reserva_y_no_del_reloj()
    {
        var a = HttpHotelBookingService.IdempotencyKeyFor("DBL/FLEX", "abc", Entrada, Salida);
        var b = HttpHotelBookingService.IdempotencyKeyFor("DBL/FLEX", "abc", Entrada, Salida);
        var otraFecha = HttpHotelBookingService.IdempotencyKeyFor("DBL/FLEX", "abc", Entrada, Salida.AddDays(1));

        Assert.Equal(a, b);
        Assert.NotEqual(a, otraFecha);
        Assert.StartsWith("stay-", a, StringComparison.Ordinal);
    }

    [Fact] // el huésped viaja seudonimizado: el orquestador no tiene por qué guardar un correo.
    public void El_huesped_viaja_seudonimizado()
    {
        var id = HttpHotelBookingService.TravellerId("Ana@Ejemplo.co");

        Assert.DoesNotContain("@", id, StringComparison.Ordinal);
        Assert.DoesNotContain("ana", id, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(id, HttpHotelBookingService.TravellerId("ana@ejemplo.co"));
    }

    [Fact] // filter: una ventana que no avanza se rechaza antes de tocar la red.
    public async Task Una_estadia_que_no_avanza_no_se_aparta()
    {
        var (svc, orq) = Nuevo();

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.HoldAsync(Peticion() with { CheckOut = Entrada }));
        Assert.Empty(orq.Llamadas);
    }

    [Fact] // un fallo de red NO puede parecerse a una reserva hecha.
    public async Task Si_el_orquestador_no_responde_NO_se_dice_que_salio_bien()
    {
        var (svc, _) = Nuevo(Feliz().Caida("POST /v1/trips"));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => svc.HoldAsync(Peticion()));
        Assert.Contains("No se te cobró", ex.Message, StringComparison.Ordinal);
    }

    // ── Cobrar ──────────────────────────────────────────────────────────────

    [Fact] // happy: confirmar deja la reserva Confirmed y ligada a la transacción.
    public async Task Cobrar_confirma_la_reserva()
    {
        var (svc, _) = Nuevo();
        var reserva = await svc.HoldAsync(Peticion());

        var pago = await svc.PayAsync(reserva.Id);

        Assert.NotNull(pago);
        Assert.Equal(HotelPaymentOutcome.Confirmed, pago!.Outcome);
        Assert.Equal(ReservationStatus.Confirmed, pago.Reservation.Status);
        Assert.Equal(900000m, pago.AmountCaptured);
        Assert.Equal(ReservationStatus.Confirmed, (await svc.GetAsync(reserva.Id))!.Status);
    }

    /// <summary>
    /// Un apartado vencido es un CONFLICTO, no un cobro — el defecto que el flujo lleva corregido.
    /// </summary>
    [Fact]
    public async Task Un_apartado_vencido_no_se_cobra()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);
        var reserva = await svc.HoldAsync(Peticion());
        orq.Falla("POST /v1/trips/tp-1/confirm", HttpStatusCode.Conflict,
            "booking.hold_expired", "El apartado venció.");

        var pago = await svc.PayAsync(reserva.Id);

        Assert.Equal(HotelPaymentOutcome.Conflict, pago!.Outcome);
        Assert.Equal(ReservationStatus.Expired, pago.Reservation.Status);
        Assert.Equal(0m, pago.AmountCaptured);
        // Y queda anotado: reintentar no vuelve a salir a la red por algo que ya se sabe.
        Assert.Equal(ReservationStatus.Expired, (await svc.GetAsync(reserva.Id))!.Status);
    }

    /// <summary>
    /// Una saga que responde 200 pero no quedó <c>Completed</c> NO confirma la habitación.
    /// </summary>
    /// <remarks>
    /// Es el caso feo: el orquestador contesta bien porque la petición fue válida, y por dentro
    /// está soltando lo que hizo. Darlo por bueno acaba con el huésped en el mostrador.
    /// </remarks>
    [Fact]
    public async Task Una_saga_que_no_quedo_Completed_NO_confirma_la_habitacion()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);
        var reserva = await svc.HoldAsync(Peticion());
        orq.Ok("POST /v1/trips/tp-1/confirm",
            ViajeCompensado.Replace("\"lastError\":null", "\"lastError\":\"el cobro fue rechazado\""));

        var pago = await svc.PayAsync(reserva.Id);

        Assert.Equal(HotelPaymentOutcome.NotCaptured, pago!.Outcome);
        Assert.Contains("rechazado", pago.FailureReason!, StringComparison.Ordinal);
        Assert.Equal(ReservationStatus.Held, (await svc.GetAsync(reserva.Id))!.Status);
    }

    [Fact] // idempotent: re-cobrar no vuelve a salir a la red.
    public async Task Re_cobrar_no_vuelve_a_llamar_al_orquestador()
    {
        var (svc, orq) = Nuevo();
        var reserva = await svc.HoldAsync(Peticion());
        await svc.PayAsync(reserva.Id);

        var otra = await svc.PayAsync(reserva.Id);

        Assert.Equal(HotelPaymentOutcome.AlreadyConfirmed, otra!.Outcome);
        Assert.Equal(1, orq.Veces("/confirm"));
    }

    [Fact] // empty: cobrar algo que este lado no anotó no inventa una reserva.
    public async Task Cobrar_lo_que_no_existe_devuelve_nulo()
    {
        var (svc, _) = Nuevo();
        Assert.Null(await svc.PayAsync("no-existe"));
        Assert.Null(await svc.CancelAsync("no-existe", null));
        Assert.Null(await svc.GetAsync("no-existe"));
    }

    // ── Cancelar ────────────────────────────────────────────────────────────

    /// <summary>
    /// La penalidad se calcula ACÁ y viaja ya calculada.
    /// </summary>
    /// <remarks>
    /// Depende de la tarifa y de cuántos días falten: es política comercial del hotel, no algo
    /// que un orquestador que sirve a hoteles, vuelos y autos deba interpretar.
    /// </remarks>
    [Fact]
    public async Task La_penalidad_se_calcula_aca_y_viaja_ya_calculada()
    {
        var (svc, orq) = Nuevo();
        var reserva = await svc.HoldAsync(Peticion());
        await svc.PayAsync(reserva.Id);

        var cancelacion = await svc.CancelAsync(reserva.Id, "cambio de planes");

        Assert.NotNull(cancelacion);
        Assert.True(cancelacion!.Refundable);
        Assert.Equal(150000m, cancelacion.PenaltyAmount);
        Assert.Equal(ReservationStatus.Cancelled, cancelacion.Reservation.Status);

        var cuerpo = orq.Llamadas.Single(l => l.Path.EndsWith("/cancel", StringComparison.Ordinal)).Body!;
        Assert.Contains("150000", cuerpo, StringComparison.Ordinal);
    }

    [Fact] // una tarifa no reembolsable retiene TODO: no se devuelve nada.
    public async Task Una_tarifa_no_reembolsable_retiene_el_total()
    {
        var (svc, orq) = Nuevo(politica: Politica(refundable: false, penalidad: 0m));
        var reserva = await svc.HoldAsync(Peticion());
        await svc.PayAsync(reserva.Id);

        await svc.CancelAsync(reserva.Id, null);

        var cuerpo = orq.Llamadas.Single(l => l.Path.EndsWith("/cancel", StringComparison.Ordinal)).Body!;
        Assert.Contains("900000", cuerpo, StringComparison.Ordinal);
    }

    [Fact] // idempotent: cancelar dos veces no devuelve dos veces.
    public async Task Cancelar_dos_veces_no_devuelve_dos_veces()
    {
        var (svc, orq) = Nuevo();
        var reserva = await svc.HoldAsync(Peticion());
        await svc.PayAsync(reserva.Id);
        await svc.CancelAsync(reserva.Id, null);

        var otra = await svc.CancelAsync(reserva.Id, null);

        Assert.NotNull(otra);
        Assert.Equal(1, orq.Veces("/cancel"));
        // Nulo y no "Refunded": esta pasada no movió dinero, y decir que sí es la clase de dato
        // con cara de verdad que ya costó una vez en este flujo.
        Assert.Null(otra!.RefundStatus);
    }

    /// <summary>
    /// Si el orquestador no pudo deshacer algo, se PROPAGA en vez de callarse.
    /// </summary>
    [Fact]
    public async Task Una_devolucion_colgada_del_orquestador_se_ve()
    {
        var orq = Feliz();
        var (svc, _) = Nuevo(orq);
        var reserva = await svc.HoldAsync(Peticion());
        await svc.PayAsync(reserva.Id);
        orq.Ok("POST /v1/trips/tp-1/cancel",
            ViajeCompensado.Replace("\"lastError\":null", "\"lastError\":\"devolución: payments caído\""));

        var cancelacion = await svc.CancelAsync(reserva.Id, null);

        Assert.StartsWith("Pendiente:", cancelacion!.RefundStatus!, StringComparison.Ordinal);
        Assert.Contains("payments caído", cancelacion.RefundStatus!, StringComparison.Ordinal);
    }
}
