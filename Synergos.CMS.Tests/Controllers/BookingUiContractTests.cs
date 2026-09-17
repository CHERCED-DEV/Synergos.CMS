using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El contrato de <c>api/booking</c> visto <b>desde quien lo consume</b>:
/// <c>&lt;synergos-booking-wizard&gt;</c> (repo <c>Synergos.UI</c>,
/// <c>apps/elements/modules/booking-wizard/</c>).
/// </summary>
/// <remarks>
/// <para><b>Lo que estos tests hacen distinto es de dónde sacan la afirmación.</b> Los 44 de
/// <see cref="BookingControllerTests"/> comprueban que el DTO lleve lo que el controller
/// decidió poner —y por eso pasaban los 44 en verde con la búsqueda devolviendo cero ofertas
/// contra un servidor real—. Acá se serializa la respuesta con las MISMAS opciones que usa
/// ASP.NET y se comprueban las claves que el normalizador del asistente lee, con sus mismas
/// reglas: si no encuentra ninguna de las que busca, DESCARTA la fila. Un DTO impecable que
/// emite otro nombre no es un detalle: es una pantalla vacía.</para>
///
/// <para>Las claves salen de <c>booking-api.client.ts</c> (<c>normalizeOffer</c>,
/// <c>normalizeOffers</c>, <c>toVoucher</c>) y de <c>booking.model.ts</c>. La UI es la fuente
/// de verdad del contrato (ADR 0083).</para>
/// </remarks>
public sealed class BookingUiContractTests
{
    private const decimal Total = 1_320_000m;
    private static readonly DateOnly CheckIn = new(2026, 9, 10);
    private static readonly DateOnly CheckOut = new(2026, 9, 13);

    private readonly IRoomAvailabilityProvider _availability = Substitute.For<IRoomAvailabilityProvider>();
    private readonly IReservationService _reservations = Substitute.For<IReservationService>();
    private readonly IPaymentProvider _payments = Substitute.For<IPaymentProvider>();
    private readonly ICancellationPolicyEvaluator _policy = Substitute.For<ICancellationPolicyEvaluator>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IAuditTrailWriter _audit = Substitute.For<IAuditTrailWriter>();

    private Reservation? _estado;

    public BookingUiContractTests()
    {
        _availability.SearchAsync(Arg.Any<AvailabilityQuery>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new RoomOffer("DBL", "Doble Superior", "FLEX", "Desayuno", Total, "COP", true, null, 5) });

        _reservations.HoldAsync(Arg.Any<ReservationRequest>(), Arg.Any<CancellationToken>())
            .Returns<Reservation>(ci =>
            {
                var req = ci.ArgAt<ReservationRequest>(0);
                _estado = new Reservation(
                    "resv_0001", ReservationStatus.Held,
                    req.RoomTypeCode, req.RatePlanCode, req.CheckIn, req.CheckOut,
                    req.GuestName, req.GuestEmail, req.TotalPrice, req.Currency,
                    PaymentSessionId: null,
                    ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(15));
                return _estado;
            });
        _reservations.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Reservation?>(ci => _estado is not null && _estado.Id == ci.ArgAt<string>(0) ? _estado : null);
        _reservations.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<Reservation>(ci =>
            {
                _estado = _estado! with { Status = ReservationStatus.Confirmed, PaymentSessionId = ci.ArgAt<string>(1) };
                return _estado;
            });

        _payments.CreateSessionAsync(Arg.Any<PaymentSessionRequest>(), Arg.Any<CancellationToken>())
            .Returns(new PaymentSession("stub_0001", PaymentStatus.Authorized, Action: null, "stub"));
        _payments.CaptureAsync(Arg.Any<string>(), Arg.Any<decimal?>(), Arg.Any<CancellationToken>())
            .Returns<PaymentOutcome>(ci => new PaymentOutcome(ci.ArgAt<string>(0), PaymentStatus.Captured, Total));

        _policy.Evaluate(Arg.Any<string>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>())
            .Returns(new CancellationOutcome(true, 220_000m, "Cancelación tardía: penalidad de 1 noche."));

        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string?>())
            .Returns(ci => $"$ {ci.ArgAt<decimal>(0)}");
    }

    private BookingController BuildSut() => new(
        _availability,
        new StubHotelBookingService(_reservations, _payments, _policy, _audit),
        _policy, _priceFormatter, NullLogger<BookingController>.Instance);

    /// <summary>
    /// El JSON tal como sale por el cable. <see cref="JsonSerializerDefaults.Web"/> es lo que
    /// configura ASP.NET, así que las claves son las que el navegador ve — no los nombres C#.
    /// </summary>
    private static readonly JsonSerializerOptions ComoAspNet = new(JsonSerializerDefaults.Web);

    private static JsonElement Wire(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        var json = JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), ComoAspNet);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>La primera de las claves que traiga texto, o null. Es el <c>??</c> de la UI.</summary>
    private static string? FirstString(JsonElement value, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (value.TryGetProperty(key, out var found)
                && found.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(found.GetString()))
            {
                return found.GetString();
            }
        }
        return null;
    }

    private static IReadOnlyList<BookingController.RoomOccupancyRequest> UnaHabitacion()
        => new[] { new BookingController.RoomOccupancyRequest(2, null) };

    // ══════════════════════ search ══════════════════════

    /// <summary>
    /// <b>El defecto que se llevaba la app entera.</b> <c>normalizeOffer</c> busca el
    /// identificador en <c>offerId</c> ?? <c>id</c> ?? <c>rateKey</c> y devuelve <c>null</c> si
    /// no está ninguno; <c>normalizeOffers</c> filtra los nulos. Este borde no emitía ni una de
    /// las tres, así que la lista de ofertas salía VACÍA siempre — y el cliente de reservas no
    /// degrada a mock a propósito, así que lo que el huésped veía era un hotel sin
    /// habitaciones. Cero excepciones, cero logs, cero tests en rojo.
    /// </summary>
    [Fact]
    public async Task Search_emite_el_identificador_con_el_que_la_UI_reconoce_una_oferta()
    {
        var oferta = Assert.Single(
            Wire(await BuildSut().Search(new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default))
                .GetProperty("offers").EnumerateArray());

        Assert.NotNull(FirstString(oferta, "offerId", "id", "rateKey"));
    }

    /// <summary>El resto de la ficha de la oferta, con las claves del contrato.</summary>
    [Fact]
    public async Task Search_emite_la_oferta_con_las_claves_que_lee_el_asistente()
    {
        var oferta = Assert.Single(
            Wire(await BuildSut().Search(new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default))
                .GetProperty("offers").EnumerateArray());

        Assert.NotNull(FirstString(oferta, "roomTypeName", "roomType"));
        // `board` (el régimen) se leía de una clave que el borde no emitía: el asistente
        // pintaba la oferta sin decir si incluye desayuno.
        Assert.Equal("Desayuno", FirstString(oferta, "board"));
        Assert.NotNull(FirstString(oferta, "totalPriceFormatted"));
        Assert.NotNull(FirstString(oferta, "currency"));
        Assert.NotNull(FirstString(oferta, "cancellationPolicy"));
        Assert.True(oferta.TryGetProperty("totalPrice", out _));
        Assert.True(oferta.TryGetProperty("refundable", out _));
        Assert.True(oferta.TryGetProperty("roomsLeft", out _));
    }

    // ══════════════════════ hold ══════════════════════

    /// <summary>
    /// <b>El cuerpo que manda el asistente, tal cual.</b> Manda <c>offerId</c> + la oferta
    /// anidada en <c>offer</c> + el huésped en <c>guest</c>; el record de la petición sólo
    /// tenía los campos planos, así que System.Text.Json no ligaba NI UNO —descarta lo que no
    /// mapea sin decir nada— y <c>hold</c> contestaba <c>400</c> siempre. El cliente devuelve
    /// <c>null</c> al fallar y el asistente lo lee como «ya no queda esa habitación»: un borde
    /// roto y un hotel lleno se veían exactamente igual.
    /// </summary>
    [Fact]
    public async Task Hold_aparta_con_el_cuerpo_que_manda_el_asistente()
    {
        var sut = BuildSut();
        var oferta = Assert.Single(
            Wire(await sut.Search(new BookingController.SearchRequest(CheckIn, CheckOut, UnaHabitacion()), default))
                .GetProperty("offers").EnumerateArray());
        var offerId = FirstString(oferta, "offerId", "id", "rateKey");

        var apartada = Wire(await sut.Hold(
            new BookingController.HoldRequest(
                CheckIn: CheckIn,
                CheckOut: CheckOut,
                Rooms: UnaHabitacion(),
                OfferId: offerId,
                Offer: new BookingController.HoldOfferRequest(
                    OfferId: offerId, TotalPrice: Total, Currency: "COP"),
                Guest: new BookingController.HoldGuestRequest("Ana Restrepo", "ana@correo.co")),
            default));

        Assert.NotNull(FirstString(apartada, "reservationId", "id"));
        // Y aparta LO QUE SE ELIGIÓ: el offerId se vuelve a partir en sus dos códigos. Sin
        // esto se podría apartar una habitación distinta de la de la pantalla.
        await _reservations.Received(1).HoldAsync(
            Arg.Is<ReservationRequest>(r =>
                r.RoomTypeCode == "DBL" && r.RatePlanCode == "FLEX"
                && r.GuestName == "Ana Restrepo" && r.GuestEmail == "ana@correo.co"
                && r.TotalPrice == Total && r.Currency == "COP"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Un <c>offerId</c> que no se entiende NO se adivina. Partirlo a medias apartaría una
    /// habitación distinta de la elegida, que es peor que no apartar ninguna.
    /// </summary>
    [Fact]
    public async Task Hold_con_un_offerId_ilegible_es_400_y_no_aparta_nada()
    {
        var result = await BuildSut().Hold(
            new BookingController.HoldRequest(
                CheckIn: CheckIn,
                CheckOut: CheckOut,
                Rooms: UnaHabitacion(),
                OfferId: "DBL",
                Guest: new BookingController.HoldGuestRequest("Ana Restrepo", "ana@correo.co"),
                Offer: new BookingController.HoldOfferRequest(TotalPrice: Total, Currency: "COP")),
            default);

        Assert.IsType<BadRequestObjectResult>(result);
        await _reservations.DidNotReceive().HoldAsync(Arg.Any<ReservationRequest>(), Arg.Any<CancellationToken>());
    }

    // ══════════════════════ pay ══════════════════════

    /// <summary>
    /// <b>El comprobante lo arma el SERVIDOR o lo arma el navegador.</b> <c>toVoucher</c> lee
    /// <c>totalPrice</c> · <c>totalPriceFormatted</c> · <c>currency</c> · <c>checkIn</c> ·
    /// <c>checkOut</c>, y las cinco faltaban: caía a los números que la propia UI traía de
    /// <c>search</c>. Mientras coincidan no se nota — y el día que el motor cobre otra cosa, el
    /// huésped se lleva impreso un total que nadie capturó, sin que nada falle.
    /// </summary>
    [Fact]
    public async Task Pay_emite_el_comprobante_con_las_claves_que_lee_la_UI()
    {
        var sut = BuildSut();
        await sut.Hold(
            new BookingController.HoldRequest(
                CheckIn: CheckIn, CheckOut: CheckOut, Rooms: UnaHabitacion(),
                RoomTypeCode: "DBL", RatePlanCode: "FLEX",
                GuestName: "Ana Restrepo", GuestEmail: "ana@correo.co",
                TotalPrice: Total, Currency: "COP"),
            default);

        var voucher = Wire(await sut.Pay(new BookingController.PayRequest("resv_0001"), default));

        Assert.Equal("Confirmed", FirstString(voucher, "status"));
        Assert.Equal(Total, voucher.GetProperty("totalPrice").GetDecimal());
        Assert.NotNull(FirstString(voucher, "totalPriceFormatted"));
        Assert.Equal("COP", FirstString(voucher, "currency"));
        Assert.Equal(CheckIn.ToString("yyyy-MM-dd"), FirstString(voucher, "checkIn"));
        Assert.Equal(CheckOut.ToString("yyyy-MM-dd"), FirstString(voucher, "checkOut"));
    }
}
