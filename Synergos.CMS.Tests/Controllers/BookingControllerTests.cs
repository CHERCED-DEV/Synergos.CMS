using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Cubre <see cref="BookingController"/> — el motor de reservas del vertical Hoteles
/// (<c>api/booking</c>: search → hold → pay → cancel + get).
/// </summary>
/// <remarks>
/// <para><b>La propiedad que protegen estos tests es que el dinero se mueva exactamente
/// cuando la respuesta dice que se movió, y solo entonces.</b> El controller es el único
/// del repo que hasta hoy no tenía ninguno, y no es código inerte: <c>pay</c> COBRA vía
/// <see cref="IPaymentProvider.CaptureAsync"/> y <c>cancel</c> DEVUELVE vía
/// <see cref="IPaymentProvider.RefundAsync"/>.</para>
///
/// <para>La regresión central es la de <c>cancel</c>. Antes de la reescritura, el endpoint
/// evaluaba la política, informaba al huésped cuánto se le reembolsaba, cancelaba la reserva
/// y <b>nunca llamaba al motor de pago</b>: la cifra era decorativa. Por eso el assert de
/// <c>Cancel_de_una_reserva_pagada_llama_a_RefundAsync_con_el_total_menos_la_penalidad</c>
/// mira la LLAMADA AL SEAM y no el JSON de la respuesta — misma lección que
/// <see cref="ShopReturnAuthTests"/>: un cuerpo que dice "reembolsado" sale idéntico con el
/// defecto abierto.</para>
///
/// <para><b>Esta cobertura encontró dos defectos que movían dinero</b>, y los dos quedaron
/// arreglados en el mismo empujón (ADR 0122): cancelar dos veces reembolsaba dos veces, y
/// pagar sobre un hold vencido capturaba la plata y después reventaba con un 500. Se
/// documentaron primero como <c>_DEFECTO</c> —describiendo lo que el controller hacía, no lo
/// que debía hacer— y los tests que quedan son ya el contrato correcto.</para>
///
/// <para>El motor se sustituye con NSubstitute, pero <see cref="IReservationService"/> se
/// cablea como una máquina de estados real sobre <c>_estado</c> (espejo fiel de
/// <c>StubReservationService</c>: Confirm liga la sesión de pago, Cancel es idempotente y NO
/// borra el <c>PaymentSessionId</c>). Sin ese detalle el doble reembolso no se ve.</para>
///
/// <para>ADR 0075 (gate de tests), ADR 0116 (motor de pagos).</para>
/// </remarks>
public sealed class BookingControllerTests
{
    private const string ReservaId = "resv_deadbeefdeadbeefdeadbeefdeadbeef";
    private const string SesionPago = "stub_cafecafecafecafecafecafecafeca";
    private const string Tarifa = "FLEX";
    private const string TarifaNoReembolsable = "PROMO-NREF";
    private const decimal Total = 1_320_000m;
    private const decimal Penalidad = 220_000m;

    private static readonly DateOnly CheckIn = new(2026, 9, 10);
    private static readonly DateOnly CheckOut = new(2026, 9, 13);

    private readonly IRoomAvailabilityProvider _availability = Substitute.For<IRoomAvailabilityProvider>();
    private readonly IReservationService _reservations = Substitute.For<IReservationService>();
    private readonly IPaymentProvider _payments = Substitute.For<IPaymentProvider>();
    private readonly ICancellationPolicyEvaluator _policy = Substitute.For<ICancellationPolicyEvaluator>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IAuditTrailWriter _audit = Substitute.For<IAuditTrailWriter>();

    /// <summary>El estado que el motor de reservas guarda entre pasos del wizard.</summary>
    private Reservation? _estado;

    public BookingControllerTests()
    {
        // ── El motor de reservas, cableado como máquina de estados ────────────────
        // Espejo de StubReservationService. Hold crea el Held, Confirm liga la sesión
        // de pago, Cancel es idempotente. Lo que se copia a propósito: Cancel NO borra
        // el PaymentSessionId — es lo que hace posible pedir el reembolso dos veces.

        _reservations.HoldAsync(Arg.Any<ReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Reservation>(ci =>
            {
                var req = ci.ArgAt<ReservationRequest>(0);
                _estado = new Reservation(
                    ReservaId, ReservationStatus.Held,
                    req.RoomTypeCode, req.RatePlanCode, req.CheckIn, req.CheckOut,
                    req.GuestName, req.GuestEmail, req.TotalPrice, req.Currency,
                    PaymentSessionId: null,
                    ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15));
                return _estado;
            });

        _reservations.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Reservation?>(ci =>
                _estado is not null && _estado.Id == ci.ArgAt<string>(0) ? _estado : null);

        _reservations.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Reservation>(ci =>
            {
                var actual = Actual();
                _estado = actual with
                {
                    Status = ReservationStatus.Confirmed,
                    PaymentSessionId = ci.ArgAt<string>(1),
                };
                return _estado;
            });

        _reservations.CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Reservation>(_ =>
            {
                _estado = Actual() with { Status = ReservationStatus.Cancelled };
                return _estado;
            });

        // ── El PSP feliz por defecto: autoriza, captura y reembolsa ───────────────
        _payments.CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentSession(SesionPago, PaymentStatus.Authorized, Action: null, "stub"));
        _payments.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns<PaymentOutcome>(ci => new PaymentOutcome(ci.ArgAt<string>(0), PaymentStatus.Captured, Total));
        _payments.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns<PaymentOutcome>(ci => new PaymentOutcome(
                ci.ArgAt<string>(0), PaymentStatus.Refunded, ci.ArgAt<decimal?>(1) ?? Total));

        // ── Política de cancelación: reembolsable con penalidad de 1 noche ────────
        _policy.Evaluate(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new CancellationOutcome(true, Penalidad, "Cancelación tardía: penalidad de 1 noche."));

        // Formateo real-ish: si el controller dejara de pasar por el formatter, los
        // asserts sobre *Formatted lo delatan (quedarían en null).
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string?>())
            .Returns(ci => $"{ci.ArgAt<decimal>(0)} {ci.ArgAt<string?>(1)}");
    }

    private BookingController BuildSut() => new(
        _availability, _reservations, _payments, _policy, _priceFormatter,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<BookingController>.Instance,
        _audit);

    /// <summary>El estado vigente, o revienta: un test que llega aquí sin reserva miente.</summary>
    private Reservation Actual() => _estado ?? throw new InvalidOperationException("Sin reserva en el motor.");

    private static IReadOnlyList<BookingController.RoomOccupancyRequest> UnaHabitacion()
        => new[] { new BookingController.RoomOccupancyRequest(2, null) };

    private static BookingController.HoldRequest HoldValido(
        string ratePlan = Tarifa, string? currency = "COP", decimal total = Total)
        => new(
            RoomTypeCode: "DBL",
            RatePlanCode: ratePlan,
            CheckIn: CheckIn,
            CheckOut: CheckOut,
            Rooms: UnaHabitacion(),
            GuestName: "Ana Restrepo",
            GuestEmail: "ana@correo.co",
            TotalPrice: total,
            Currency: currency);

    /// <summary>Deja el motor con una reserva CONFIRMADA y pagada (la vía normal a cancel).</summary>
    private void ReservaPagada(string ratePlan = Tarifa)
        => _estado = new Reservation(
            ReservaId, ReservationStatus.Confirmed, "DBL", ratePlan, CheckIn, CheckOut,
            "Ana Restrepo", "ana@correo.co", Total, "COP", PaymentSessionId: SesionPago);

    /// <summary>Deja el motor con una reserva apartada y SIN cobrar.</summary>
    /// <summary>
    /// Un hold VIGENTE por defecto. <paramref name="expiresAt"/> lo vence a propósito.
    /// </summary>
    /// <remarks>
    /// El default estaba en el pasado y ningún test lo notaba, porque el controller no miraba
    /// el vencimiento antes de cobrar — que era justo el defecto. Al cerrarlo, un hold vencido
    /// por defecto habría hecho que todos los pagos respondieran 409.
    /// </remarks>
    private void ReservaApartadaSinPagar(
        ReservationStatus status = ReservationStatus.Held,
        DateTimeOffset? expiresAt = null)
        => _estado = new Reservation(
            ReservaId, status, "DBL", Tarifa, CheckIn, CheckOut,
            "Ana Restrepo", "ana@correo.co", Total, "COP", PaymentSessionId: null,
            ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15));

    private static T Body<T>(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return Assert.IsType<T>(ok.Value);
    }

    // ══════════════════════ 1. SEARCH ══════════════════════

    [Fact] // happy: cada oferta sale con su precio formateado y el texto de su política
    public async Task Search_con_rango_valido_enriquece_cada_oferta_con_precio_y_politica()
    {
        _availability.SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new RoomOffer("DBL", "Doble Superior", Tarifa, "Desayuno", Total, "COP", true, 2, 3),
            });

        var result = await BuildSut().Search(
            new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default);

        var body = Body<BookingController.SearchResponse>(result);
        var oferta = Assert.Single(body.Offers);
        Assert.Equal("DBL", oferta.RoomTypeCode);
        Assert.Equal($"{Total} COP", oferta.TotalPriceFormatted);
        Assert.Equal("Cancelación tardía: penalidad de 1 noche.", oferta.CancellationPolicy);
        Assert.Equal(3, oferta.RoomsLeft);

        // La política se evalúa POR RATE PLAN y contra el check-in de la búsqueda: una
        // tarifa evaluada con las fechas de otra le miente al huésped en el listado.
        _policy.Received(1).Evaluate(Tarifa, CheckIn, Arg.Any<DateOnly>());
    }

    [Fact] // empty: sin disponibilidad la respuesta es una lista vacía, no un 404
    public async Task Search_sin_disponibilidad_devuelve_lista_vacia()
    {
        _availability.SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RoomOffer>());

        var body = Body<BookingController.SearchResponse>(await BuildSut().Search(
            new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default));

        Assert.Empty(body.Offers);
    }

    [Fact]
    public async Task Search_sin_cuerpo_es_400_y_no_toca_disponibilidad()
    {
        Assert.IsType<BadRequestObjectResult>(await BuildSut().Search(null, default));
        await _availability.DidNotReceive().SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact] // filter: el rango invertido se rechaza ANTES de consultar al PMS
    public async Task Search_con_salida_anterior_a_la_entrada_es_400_y_no_toca_disponibilidad()
    {
        var result = await BuildSut().Search(
            new BookingController.SearchRequest(CheckOut, CheckIn, UnaHabitacion()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        await _availability.DidNotReceive().SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact] // el mismo día de entrada y salida tampoco es una noche
    public async Task Search_con_entrada_igual_a_salida_es_400()
    {
        Assert.IsType<BadRequestObjectResult>(await BuildSut().Search(
            new BookingController.SearchRequest(CheckIn, CheckIn, UnaHabitacion()), default));
    }

    [Fact] // empty: sin ocupación no hay nada que buscar
    public async Task Search_sin_habitaciones_es_400_y_no_toca_disponibilidad()
    {
        var result = await BuildSut().Search(
            new BookingController.SearchRequest(CheckIn, CheckOut, Array.Empty<BookingController.RoomOccupancyRequest>()), default);

        Assert.IsType<BadRequestObjectResult>(result);
        await _availability.DidNotReceive().SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>());
    }

    [Fact] // Rooms null en el JSON entra como null, no como lista vacía: no debe reventar
    public async Task Search_con_Rooms_null_es_400_y_no_lanza()
    {
        Assert.IsType<BadRequestObjectResult>(await BuildSut().Search(
            new BookingController.SearchRequest(CheckIn, CheckOut, null), default));
    }

    [Fact] // el rechazo del motor se traduce a 400, no escapa como 500
    public async Task Search_traduce_el_ArgumentException_del_motor_a_400()
    {
        _availability.SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<RoomOffer>>(_ => throw new ArgumentException("Rango inválido."));

        Assert.IsType<BadRequestObjectResult>(await BuildSut().Search(
            new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default));
    }

    // ══════════════════════ 2. HOLD ══════════════════════

    [Fact] // happy: aparta en Held, con los datos del huésped ya trimeados
    public async Task Hold_valido_aparta_la_reserva_en_Held()
    {
        var body = Body<BookingController.ReservationResponse>(
            await BuildSut().Hold(HoldValido(), default));

        Assert.Equal(ReservaId, body.ReservationId);
        Assert.Equal(nameof(ReservationStatus.Held), body.Status);
        Assert.Null(body.PaymentSessionId);   // apartar no cobra
        Assert.Equal($"{Total} COP", body.TotalPriceFormatted);
    }

    [Fact] // sin moneda el motor asume COP — no una cadena vacía que rompa al PSP
    public async Task Hold_sin_moneda_asume_COP()
    {
        await BuildSut().Hold(HoldValido(currency: "   "), default);

        await _reservations.Received(1).HoldAsync(
            Arg.Is<ReservationRequest>(r => r.Currency == "COP"), Arg.Any<CancellationToken>());
    }

    [Fact] // los campos llegan trimeados al motor: " DBL " y "DBL" no son dos productos
    public async Task Hold_trimea_los_codigos_y_los_datos_del_huesped()
    {
        var request = HoldValido() with { RoomTypeCode = "  DBL  ", GuestEmail = " ana@correo.co " };

        await BuildSut().Hold(request, default);

        await _reservations.Received(1).HoldAsync(
            Arg.Is<ReservationRequest>(r => r.RoomTypeCode == "DBL" && r.GuestEmail == "ana@correo.co"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Hold_sin_cuerpo_es_400_y_no_aparta_nada()
    {
        Assert.IsType<BadRequestObjectResult>(await BuildSut().Hold(null, default));
        await _reservations.DidNotReceive().HoldAsync(Arg.Any<ReservationRequest>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Los rechazos de <c>hold</c>. Van juntos porque son la misma propiedad: NADA
    /// inválido llega a apartar cupo. El assert que importa es el <c>DidNotReceive</c>
    /// — un 400 sale igual aunque el cuerpo del método haya seguido ejecutando.
    /// </summary>
    public static TheoryData<string, BookingController.HoldRequest> HoldsInvalidos() => new()
    {
        { "salida antes de la entrada", HoldValido() with { CheckIn = CheckOut, CheckOut = CheckIn } },
        { "sin room type", HoldValido() with { RoomTypeCode = "   " } },
        { "sin rate plan", HoldValido() with { RatePlanCode = "" } },
        { "sin nombre del huésped", HoldValido() with { GuestName = "  " } },
        { "sin correo del huésped", HoldValido() with { GuestEmail = "" } },
        { "total en cero", HoldValido(total: 0m) },
        { "total negativo", HoldValido(total: -1m) },
        { "sin habitaciones", HoldValido() with { Rooms = Array.Empty<BookingController.RoomOccupancyRequest>() } },
        { "Rooms null", HoldValido() with { Rooms = null } },
    };

    [Theory]
    [MemberData(nameof(HoldsInvalidos))]
    public async Task Hold_invalido_es_400_y_no_aparta_cupo(string caso, BookingController.HoldRequest request)
    {
        var result = await BuildSut().Hold(request, default);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.False(string.IsNullOrEmpty(caso));
        await _reservations.DidNotReceive().HoldAsync(Arg.Any<ReservationRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact] // el rechazo del motor se traduce a 400, no escapa como 500
    public async Task Hold_traduce_el_ArgumentException_del_motor_a_400()
    {
        _reservations.HoldAsync(Arg.Any<ReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Reservation>(_ => throw new ArgumentException("Sin cupo."));

        Assert.IsType<BadRequestObjectResult>(await BuildSut().Hold(HoldValido(), default));
    }

    // ══════════════════════ 3. PAY ══════════════════════

    [Fact] // EL FLUJO ENTERO: search → hold → pay → get, y lo pagado se lee Confirmed
    public async Task Flujo_completo_search_hold_pay_get_deja_la_reserva_Confirmed()
    {
        _availability.SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new RoomOffer("DBL", "Doble Superior", Tarifa, "Desayuno", Total, "COP", true, null, 5) });
        var sut = BuildSut();

        var oferta = Assert.Single(
            Body<BookingController.SearchResponse>(await sut.Search(
                new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default)).Offers);

        var apartada = Body<BookingController.ReservationResponse>(await sut.Hold(
            HoldValido(ratePlan: oferta.RatePlanCode) with { TotalPrice = oferta.TotalPrice }, default));
        Assert.Equal(nameof(ReservationStatus.Held), apartada.Status);

        var pagada = Body<BookingController.PayResponse>(await sut.Pay(
            new BookingController.PayRequest(apartada.ReservationId), default));
        Assert.Equal(nameof(ReservationStatus.Confirmed), pagada.Status);
        Assert.Equal(nameof(PaymentStatus.Captured), pagada.PaymentStatus);
        Assert.Equal(SesionPago, pagada.PaymentSessionId);
        Assert.Equal(Total, pagada.AmountCaptured);
        Assert.Null(pagada.FailureReason);

        // La lectura posterior es la que ve el huésped en su voucher: si el motor no
        // guardó la confirmación, aquí seguiría diciendo "Held" con el cobro hecho.
        var leida = Body<BookingController.ReservationResponse>(
            await sut.Get(apartada.ReservationId, default));
        Assert.Equal(nameof(ReservationStatus.Confirmed), leida.Status);
        Assert.Equal(SesionPago, leida.PaymentSessionId);

        // Se cobró el total de LA RESERVA, una sola vez.
        await _payments.Received(1).CreateSessionAsync(
            Arg.Is<PaymentSessionRequest>(r => r.Amount == Total && r.OrderReference == ReservaId && r.Currency == "COP"),
            Arg.Any<CancellationToken>());
        await _payments.Received(1).CaptureAsync(SesionPago, Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // idempotente: pagar dos veces no cobra dos veces
    public async Task Pay_de_una_reserva_ya_confirmada_no_vuelve_a_cobrar()
    {
        ReservaPagada();

        var result = await BuildSut().Pay(new BookingController.PayRequest(ReservaId), default);

        // Nota de contrato: la segunda llamada responde con la forma ReservationResponse,
        // no con PayResponse. La UI que lea `paymentStatus` del reintento no lo encuentra.
        var body = Body<BookingController.ReservationResponse>(result);
        Assert.Equal(nameof(ReservationStatus.Confirmed), body.Status);
        await _payments.DidNotReceive().CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>());
        await _payments.DidNotReceive().CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // una reserva cancelada no se cobra: el cupo ya volvió al inventario
    public async Task Pay_de_una_reserva_cancelada_es_400_y_no_abre_sesion_de_pago()
    {
        ReservaPagada();
        _estado = Actual() with { Status = ReservationStatus.Cancelled };

        var result = await BuildSut().Pay(new BookingController.PayRequest(ReservaId), default);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var body = Assert.IsType<BookingController.PayResponse>(bad.Value);
        Assert.Equal(nameof(PaymentStatus.Cancelled), body.PaymentStatus);
        Assert.Equal(0m, body.AmountCaptured);
        await _payments.DidNotReceive().CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact] // EL QUE MUERDE en pay: si el cobro falla, la reserva NO queda confirmada
    public async Task Pay_cuando_la_captura_falla_no_confirma_la_reserva()
    {
        ReservaApartadaSinPagar();
        _payments.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome(SesionPago, PaymentStatus.Failed, 0m, "Tarjeta rechazada."));

        var body = Body<BookingController.PayResponse>(
            await BuildSut().Pay(new BookingController.PayRequest(ReservaId), default));

        Assert.Equal(nameof(PaymentStatus.Failed), body.PaymentStatus);
        Assert.Equal(nameof(ReservationStatus.Held), body.Status);   // sigue apartada, no confirmada
        Assert.Equal(0m, body.AmountCaptured);
        Assert.Equal("Tarjeta rechazada.", body.FailureReason);
        await _reservations.DidNotReceive().ConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Assert.Equal(ReservationStatus.Held, Actual().Status);
    }

    [Fact] // 3DS / PSE / Nequi: falta una acción del cliente, y se le dice cuál sin confirmar
    public async Task Pay_cuando_el_pago_requiere_accion_del_cliente_no_confirma_y_lo_explica()
    {
        ReservaApartadaSinPagar();
        _payments.CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentSession(
                SesionPago, PaymentStatus.RequiresAction,
                PaymentAction.AwaitApproval("Aprobá la compra en tu app Nequi."), "stub"));
        _payments.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome(SesionPago, PaymentStatus.RequiresAction, 0m));

        var body = Body<BookingController.PayResponse>(
            await BuildSut().Pay(new BookingController.PayRequest(ReservaId), default));

        Assert.Equal(nameof(PaymentStatus.RequiresAction), body.PaymentStatus);
        Assert.Equal(nameof(ReservationStatus.Held), body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.FailureReason));   // no se calla el porqué
        await _reservations.DidNotReceive().ConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Pay_de_una_reserva_inexistente_es_404_y_no_abre_sesion_de_pago()
    {
        var result = await BuildSut().Pay(new BookingController.PayRequest("resv_fantasma"), default);

        Assert.IsType<NotFoundObjectResult>(result);
        await _payments.DidNotReceive().CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Pay_sin_reservationId_es_400_y_no_consulta_el_motor(string? id)
    {
        var request = id is null ? null : new BookingController.PayRequest(id);

        Assert.IsType<BadRequestObjectResult>(await BuildSut().Pay(request, default));
        await _reservations.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(ReservationStatus.Held)]     // vigente en el papel, vencido en el reloj
    [InlineData(ReservationStatus.Expired)]  // ya marcado por el escáner de vencimientos
    public async Task Pay_sobre_un_hold_vencido_es_409_y_NO_abre_la_caja(ReservationStatus status)
    {
        // Antes: las guardas cubrían Confirmed y Cancelled, y un hold vencido no es ninguna
        // de las dos. Caía derecho a CreateSession + Capture —dinero capturado— y solo
        // entonces ConfirmAsync lanzaba porque el hold ya no valía. Nadie atrapaba eso: 500,
        // huésped cobrado, sin reserva y sin compensación.
        //
        // El cupo se verifica primero, la caja se abre después.
        ReservaApartadaSinPagar(status, expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var result = await BuildSut().Pay(new BookingController.PayRequest(ReservaId), default);

        var conflict = Assert.IsType<ConflictObjectResult>(result);
        var body = Assert.IsType<BookingController.PayResponse>(conflict.Value);
        Assert.Equal(nameof(ReservationStatus.Expired), body.Status);
        Assert.Equal(0m, body.AmountCaptured);
        Assert.False(string.IsNullOrWhiteSpace(body.FailureReason));

        // Lo que importa: NO se abrió sesión ni se capturó nada.
        await _payments.DidNotReceive().CreateSessionAsync(
            Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>());
        await _payments.DidNotReceive().CaptureAsync(
            Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().ConfirmAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Un_hold_SIN_vencimiento_declarado_no_se_trata_como_vencido()
    {
        // ExpiresAt es nullable. Sin fecha no hay vencimiento que comprobar, y tratarlo como
        // vencido cerraría el cobro de toda reserva que no declare ventana.
        ReservaApartadaSinPagar();
        _estado = _estado! with { ExpiresAt = null };

        var result = await BuildSut().Pay(new BookingController.PayRequest(ReservaId), default);

        Assert.IsNotType<ConflictObjectResult>(result);
        await _payments.Received(1).CreateSessionAsync(
            Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>());
    }

    // ══════════════════════ 4. CANCEL ══════════════════════

    [Fact] // LA REGRESIÓN CENTRAL: cancelar DEVUELVE la plata, no solo la calcula
    public async Task Cancel_de_una_reserva_pagada_llama_a_RefundAsync_con_el_total_menos_la_penalidad()
    {
        ReservaPagada();

        var body = Body<BookingController.CancelResponse>(
            await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        // El assert que importa: la LLAMADA al motor de pago. El cuerpo de la respuesta
        // salía idéntico cuando el reembolso era una cifra decorativa que nadie ejecutaba.
        await _payments.Received(1).RefundAsync(SesionPago, Total - Penalidad, Arg.Any<CancellationToken>());

        Assert.Equal(nameof(ReservationStatus.Cancelled), body.Status);
        Assert.Equal(nameof(PaymentStatus.Refunded), body.RefundStatus);
        Assert.True(body.Refundable);
        Assert.Equal(Penalidad, body.PenaltyAmount);
    }

    [Fact] // cancelar a tiempo: sin penalidad se devuelve el total íntegro
    public async Task Cancel_sin_penalidad_reembolsa_el_total_completo()
    {
        ReservaPagada();
        _policy.Evaluate(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new CancellationOutcome(true, 0m, "Cancelación gratuita."));

        await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default);

        await _payments.Received(1).RefundAsync(SesionPago, Total, Arg.Any<CancellationToken>());
    }

    [Fact] // la política se evalúa con las fechas DE LA RESERVA y contra hoy (UTC)
    public async Task Cancel_evalua_la_politica_del_rate_plan_de_la_reserva_al_dia_de_hoy()
    {
        ReservaPagada();
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);

        await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default);

        _policy.Received(1).Evaluate(Tarifa, CheckIn, hoy);
    }

    [Fact] // filter: tarifa no reembolsable → se cancela, pero NO se toca el motor de pago
    public async Task Cancel_de_una_tarifa_no_reembolsable_no_devuelve_nada()
    {
        ReservaPagada(TarifaNoReembolsable);
        // Penalidad MENOR que el total a propósito (es lo que hace StubCancellationPolicy-
        // Evaluator: penalidad simbólica de 1 noche también en la no reembolsable). Así lo
        // único que impide el reembolso es la bandera Refundable. Con penalidad == total,
        // este test pasaba aunque se quitara la bandera: lo frenaba el `refundable > 0`.
        _policy.Evaluate(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new CancellationOutcome(false, Penalidad, "Tarifa no reembolsable."));

        var body = Body<BookingController.CancelResponse>(
            await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        await _payments.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
        Assert.False(body.Refundable);
        Assert.Null(body.RefundStatus);   // null = no había nada que devolver, y se dice
        Assert.Equal(nameof(ReservationStatus.Cancelled), body.Status);
    }

    [Fact] // filter: si la penalidad se come el total no hay transferencia de 0 pesos al PSP
    public async Task Cancel_cuando_la_penalidad_iguala_al_total_no_llama_al_motor_de_pago()
    {
        ReservaPagada();
        _policy.Evaluate(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new CancellationOutcome(true, Total, "Cancelación fuera de plazo."));

        var body = Body<BookingController.CancelResponse>(
            await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        await _payments.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
        Assert.Null(body.RefundStatus);
    }

    [Fact] // empty: una reserva que nunca se cobró se libera sin pedirle nada al PSP
    public async Task Cancel_de_una_reserva_nunca_pagada_libera_el_cupo_sin_reembolsar()
    {
        ReservaApartadaSinPagar();

        var body = Body<BookingController.CancelResponse>(
            await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        await _reservations.Received(1).CancelAsync(ReservaId, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payments.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
        Assert.Equal(nameof(ReservationStatus.Cancelled), body.Status);
        Assert.Null(body.RefundStatus);
    }

    [Fact] // EL QUE MUERDE en cancel: si el PSP no devolvió, la respuesta NO dice que devolvió
    public async Task Cancel_cuando_el_reembolso_falla_no_declara_el_dinero_devuelto()
    {
        ReservaPagada();
        _payments.RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentOutcome(SesionPago, PaymentStatus.Failed, 0m, "El PSP rechazó el reembolso."));

        var body = Body<BookingController.CancelResponse>(
            await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        // RefundStatus es el ÚNICO campo que distingue "te devolvimos" de "lo intentamos":
        // si dijera Refunded (o se quedara en null, que significa "no había nada que
        // devolver"), la respuesta sería exactamente la mentira que la reescritura cerró.
        Assert.Equal(nameof(PaymentStatus.Failed), body.RefundStatus);
        Assert.NotEqual(nameof(PaymentStatus.Refunded), body.RefundStatus);
        // La cancelación no se deshace: el cupo ya volvió al inventario.
        Assert.Equal(nameof(ReservationStatus.Cancelled), body.Status);
    }

    [Fact]
    public async Task Cancelar_dos_veces_reembolsa_UNA_sola_vez()
    {
        // Antes: la guarda del reembolso miraba la política y el PaymentSessionId, nunca el
        // ESTADO — y CancelAsync es idempotente pero no borra ese id, así que la segunda
        // llamada volvía a pedirle al PSP la misma devolución.
        //
        // No se duplicaba plata de verdad solo porque el proveedor stub reembolsa únicamente
        // sesiones capturadas y en la segunda pasada encuentra Refunded: el PSP salvaba al
        // controller. Una pasarela que acepte reembolsos parciales sucesivos —el caso normal
        // con penalidad, porque quedan pesos sin devolver en la sesión— sí paga dos veces.
        ReservaPagada();
        var sut = BuildSut();

        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);
        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);

        await _payments.Received(1).RefundAsync(SesionPago, Total - Penalidad, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_segunda_cancelacion_responde_200_y_no_declara_un_reembolso_que_no_hizo()
    {
        // 200 y no error: cancelar lo ya cancelado es el resultado que el huésped pidió, y
        // reintentar tras un timeout de red no puede parecer un fallo. Pero el RefundStatus
        // va en null, porque esta pasada no movió dinero — afirmar un reembolso que no
        // ocurrió aquí es la clase de dato con cara de verdad que ya costó una vez en este
        // mismo endpoint.
        ReservaPagada();
        var sut = BuildSut();

        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);
        var segunda = Body<BookingController.CancelResponse>(
            await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        Assert.Equal(nameof(ReservationStatus.Cancelled), segunda.Status);
        Assert.Null(segunda.RefundStatus);
        Assert.Equal(Penalidad, segunda.PenaltyAmount);
    }

    [Fact]
    public async Task La_segunda_cancelacion_tampoco_vuelve_a_cancelar_en_el_motor()
    {
        ReservaPagada();
        var sut = BuildSut();

        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);
        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);

        await _reservations.Received(1).CancelAsync(
            ReservaId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancelar_deja_rastro_de_auditoria()
    {
        // El endpoint es anónimo —el reservationId ES la credencial— y a la vez destructivo
        // y con movimiento de plata. Sin rastro no hay forma de responder "¿quién canceló
        // esta estadía y cuándo?", que es la pregunta que llega cuando alguien reenvía un
        // correo de confirmación. Auditarlo no rompe la compra de invitado.
        ReservaPagada();

        await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, "cambio de planes"), default);

        await _audit.Received(1).WriteAsync(
            Arg.Is<AuditEvent>(e =>
                e.Action == "booking.reservation.cancelled"
                && e.Resource == ReservaId
                && e.Detail.Contains("cambio de planes")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Si_la_auditoria_falla_la_cancelacion_NO_se_deshace()
    {
        // La reserva ya quedó cancelada y el reembolso ya salió: desandar eso porque no se
        // pudo escribir una línea de log sería el remedio peor que la enfermedad.
        ReservaPagada();
        _audit.WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new IOException("disco lleno"));

        var body = Body<BookingController.CancelResponse>(
            await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default));

        Assert.Equal(nameof(ReservationStatus.Cancelled), body.Status);
        await _payments.Received(1).RefundAsync(SesionPago, Total - Penalidad, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task La_segunda_cancelacion_no_vuelve_a_auditar()
    {
        // Un rastro por cancelación real. Repetir la entrada en cada reintento de red
        // convertiría la auditoría en ruido y haría parecer que se canceló dos veces.
        ReservaPagada();
        var sut = BuildSut();

        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);
        await sut.Cancel(new BookingController.CancelRequest(ReservaId, null), default);

        await _audit.Received(1).WriteAsync(Arg.Any<AuditEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_de_una_reserva_inexistente_es_404_y_no_reembolsa()
    {
        var result = await BuildSut().Cancel(new BookingController.CancelRequest("resv_fantasma", null), default);

        Assert.IsType<NotFoundObjectResult>(result);
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _payments.DidNotReceive().RefundAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_sin_reservationId_es_400_y_no_consulta_el_motor(string? id)
    {
        var request = id is null ? null : new BookingController.CancelRequest(id, null);

        Assert.IsType<BadRequestObjectResult>(await BuildSut().Cancel(request, default));
        await _reservations.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory] // el motivo queda en la bitácora del motor: sin motivo, uno por defecto
    [InlineData(null, "guest-requested")]
    [InlineData("   ", "guest-requested")]
    [InlineData("  cambio de planes  ", "cambio de planes")]
    public async Task Cancel_registra_el_motivo_normalizado(string? enviado, string esperado)
    {
        ReservaApartadaSinPagar();

        await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, enviado), default);

        await _reservations.Received(1).CancelAsync(ReservaId, esperado, Arg.Any<CancellationToken>());
    }

    // ══════════════════════ 5. GET ══════════════════════

    [Fact]
    public async Task Get_de_una_reserva_existente_la_devuelve_con_su_precio_formateado()
    {
        ReservaPagada();

        var body = Body<BookingController.ReservationResponse>(await BuildSut().Get(ReservaId, default));

        Assert.Equal(ReservaId, body.ReservationId);
        Assert.Equal(nameof(ReservationStatus.Confirmed), body.Status);
        Assert.Equal(SesionPago, body.PaymentSessionId);
        Assert.Equal($"{Total} COP", body.TotalPriceFormatted);
    }

    [Fact] // idempotente: leer dos veces no muta nada ni cambia la respuesta
    public async Task Get_es_idempotente()
    {
        ReservaPagada();
        var sut = BuildSut();

        var primera = Body<BookingController.ReservationResponse>(await sut.Get(ReservaId, default));
        var segunda = Body<BookingController.ReservationResponse>(await sut.Get(ReservaId, default));

        Assert.Equal(primera, segunda);   // records: igualdad estructural
        await _reservations.Received(2).GetAsync(ReservaId, Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _reservations.DidNotReceive().CancelAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // un id desconocido es 404, no un 200 con cuerpo vacío
    public async Task Get_de_una_reserva_inexistente_es_404()
        => Assert.IsType<NotFoundObjectResult>(await BuildSut().Get("resv_fantasma", default));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Get_con_id_vacio_es_400_y_no_consulta_el_motor(string id)
    {
        Assert.IsType<BadRequestObjectResult>(await BuildSut().Get(id, default));
        await _reservations.DidNotReceive().GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // ══════════════════════ Superficie de autorización ══════════════════════

    [Fact]
    public async Task Cancel_no_pide_sesion_el_id_de_reserva_ES_la_credencial()
    {
        // Regresión DELIBERADA, con la misma forma que TravelControllerAuthTests fija
        // para `order/{orderRef}`: el controller no recibe IMemberAccessGate y el id es
        // `resv_{Guid:N}` —128 bits, inadivinable—, así que tenerlo ES la autorización.
        // Es lo que permite que el huésped que reservó como invitado vuelva a su reserva.
        //
        // NO es la forma del hueco de `shop/return/{rmaId}/advance`: allí el endpoint
        // caía abierto mientras sus DOS VECINOS sí comprobaban dueño — una asimetría
        // dentro del mismo controller. Aquí las cinco rutas son públicas por diseño y
        // el <remarks> del controller lo declara.
        //
        // La diferencia que sí queda anotada: a diferencia de `order/{ref}` (lectura),
        // esta ruta MUEVE DINERO. Si algún día el id de reserva viaja por correo, log o
        // Referer, quien lo lea puede cancelar y disparar el reembolso.
        ReservaPagada();

        var result = await BuildSut().Cancel(new BookingController.CancelRequest(ReservaId, null), default);

        Assert.IsNotType<UnauthorizedObjectResult>(result);
        Assert.IsType<OkObjectResult>(result);
    }
}
