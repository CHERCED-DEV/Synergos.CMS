using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del MOTOR transaccional multi-producto (dominio Booking — doc
/// booking-app-spec). Es el arquetipo del flujo unificado seleccionar → pagar
/// (una sola vez) → confirmar para un CARRITO DE VIAJE con ítems heterogéneos
/// (hotel + vuelo + auto). Delega a los 3 providers de búsqueda
/// (<see cref="IRoomAvailabilityProvider"/>, <see cref="IFlightAvailabilityProvider"/>,
/// <see cref="ICarRentalProvider"/>) y orquesta el checkout/confirm vía
/// <see cref="ITravelCartService"/>, formateando precios es-CO con
/// <see cref="IPriceFormatter"/>.
/// </summary>
/// <remarks>
/// Coexiste con <c>BookingController</c> (api/booking, flujo hotel de un solo
/// producto) sin duplicarlo ni tocarlo: este controller es el caso GENERAL
/// (N ítems, un pago), aquél es el caso N=1 del vertical Hoteles. La capa Web
/// SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en los seams
/// (Application, sin Umbraco — ADR 0002). Los seams se cambian por adapters
/// reales sin tocar este controller. API pública (sin auth-gate): el orderRef es
/// la credencial para confirm.
/// </remarks>
[ApiController]
[Route("api/travel")]
public sealed class TravelController : ControllerBase
{
    private readonly IRoomAvailabilityProvider _hotels;
    private readonly IFlightAvailabilityProvider _flights;
    private readonly ICarRentalProvider _cars;
    private readonly ITravelCartService _cart;
    private readonly IPriceFormatter _priceFormatter;

    public TravelController(
        IRoomAvailabilityProvider hotels,
        IFlightAvailabilityProvider flights,
        ICarRentalProvider cars,
        ITravelCartService cart,
        IPriceFormatter priceFormatter)
    {
        _hotels = hotels;
        _flights = flights;
        _cars = cars;
        _cart = cart;
        _priceFormatter = priceFormatter;
    }

    // ── 1. Search hotel ────────────────────────────────────────────
    // GET /api/travel/search/hotel?checkIn=&checkOut=&adults=&children=
    [HttpGet("search/hotel")]
    public async Task<IActionResult> SearchHotel(
        [FromQuery] DateOnly checkIn,
        [FromQuery] DateOnly checkOut,
        [FromQuery] int adults = 2,
        [FromQuery] int children = 0,
        CancellationToken cancellationToken = default)
    {
        if (checkOut <= checkIn)
        {
            return BadRequest(new { error = "checkOut debe ser posterior a checkIn." });
        }

        var rooms = new[] { new RoomOccupancy(Math.Max(1, adults), BuildChildAges(children)) };

        IReadOnlyList<RoomOffer> offers;
        try
        {
            offers = await _hotels.SearchAsync(new AvailabilityQuery(checkIn, checkOut, rooms), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var results = offers.Select(o => new HotelOfferDto(
            OfferId: $"{o.RoomTypeCode}/{o.RatePlanCode}",
            Product: TravelProductType.Hotel.ToString(),
            RoomTypeCode: o.RoomTypeCode,
            RoomTypeName: o.RoomTypeName,
            RatePlanCode: o.RatePlanCode,
            BoardBasis: o.BoardBasis,
            Refundable: o.Refundable,
            RoomsLeft: o.RoomsLeft,
            Price: o.TotalPrice,
            PriceFormatted: _priceFormatter.Format(o.TotalPrice, o.Currency),
            Currency: o.Currency)).ToList();

        return Ok(new { offers = results });
    }

    // ── 2. Search flight ───────────────────────────────────────────
    // GET /api/travel/search/flight?origin=&destination=&departDate=&returnDate=&adults=&children=&infants=
    [HttpGet("search/flight")]
    public async Task<IActionResult> SearchFlight(
        [FromQuery] string origin,
        [FromQuery] string destination,
        [FromQuery] DateOnly departDate,
        [FromQuery] DateOnly? returnDate = null,
        [FromQuery] int adults = 1,
        [FromQuery] int children = 0,
        [FromQuery] int infants = 0,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
        {
            return BadRequest(new { error = "origin y destination son requeridos." });
        }

        IReadOnlyList<FlightOption> options;
        try
        {
            options = await _flights.SearchAsync(
                new FlightSearchQuery(origin.Trim(), destination.Trim(), departDate, returnDate, Math.Max(1, adults), children, infants),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var results = options.SelectMany(o => o.Fares.Select(f => new FlightOfferDto(
            OfferId: f.FareCode,
            Product: TravelProductType.Flight.ToString(),
            FlightId: o.FlightId,
            Origin: o.Segments[0].Origin,
            Destination: o.Segments[^1].Destination,
            Stops: o.Stops,
            TotalDurationMinutes: o.TotalDurationMinutes,
            Cabin: f.Cabin,
            FamilyName: f.FamilyName,
            Refundable: f.Refundable,
            BaggageIncluded: f.BaggageIncluded,
            Price: f.TotalPrice,
            PriceFormatted: _priceFormatter.Format(f.TotalPrice, f.Currency),
            Currency: f.Currency))).ToList();

        return Ok(new { offers = results });
    }

    // ── 3. Search car ──────────────────────────────────────────────
    // GET /api/travel/search/car?pickupLocation=&dropoffLocation=&pickupDate=&dropoffDate=
    [HttpGet("search/car")]
    public async Task<IActionResult> SearchCar(
        [FromQuery] string pickupLocation,
        [FromQuery] DateOnly pickupDate,
        [FromQuery] DateOnly dropoffDate,
        [FromQuery] string? dropoffLocation = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pickupLocation))
        {
            return BadRequest(new { error = "pickupLocation es requerido." });
        }
        if (dropoffDate <= pickupDate)
        {
            return BadRequest(new { error = "dropoffDate debe ser posterior a pickupDate." });
        }

        IReadOnlyList<CarOffer> offers;
        try
        {
            offers = await _cars.SearchAsync(
                new CarRentalQuery(pickupLocation.Trim(), dropoffLocation?.Trim(), pickupDate, dropoffDate),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var results = offers.Select(c => new CarOfferDto(
            OfferId: c.CategoryCode,
            Product: TravelProductType.Car.ToString(),
            CategoryName: c.CategoryName,
            SippCode: c.SippCode,
            Provider: c.Provider,
            Transmission: c.Transmission,
            AirConditioning: c.AirConditioning,
            Days: c.Days,
            PricePerDayFormatted: _priceFormatter.Format(c.PricePerDay, c.Currency),
            Price: c.TotalPrice,
            PriceFormatted: _priceFormatter.Format(c.TotalPrice, c.Currency),
            Currency: c.Currency)).ToList();

        return Ok(new { offers = results });
    }

    // ── 4. Checkout ────────────────────────────────────────────────
    // POST /api/travel/checkout { items:[{product,offerId,label,price,currency}], guest:{name,email} }
    [HttpPost("checkout")]
    public async Task<IActionResult> Checkout(
        [FromBody] CheckoutRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
        {
            return BadRequest(new { error = "Se requiere al menos un ítem en el carrito." });
        }
        if (request.Guest is null || string.IsNullOrWhiteSpace(request.Guest.Name) || string.IsNullOrWhiteSpace(request.Guest.Email))
        {
            return BadRequest(new { error = "Se requieren el nombre y el email del viajero." });
        }

        var items = new List<TravelCartItem>(request.Items.Count);
        foreach (var dto in request.Items)
        {
            if (!Enum.TryParse<TravelProductType>(dto.Product, ignoreCase: true, out var product))
            {
                return BadRequest(new { error = $"Producto desconocido: '{dto.Product}'." });
            }
            var currency = string.IsNullOrWhiteSpace(dto.Currency) ? "COP" : dto.Currency.Trim();
            items.Add(new TravelCartItem(product, dto.OfferId?.Trim() ?? string.Empty, dto.Label?.Trim() ?? string.Empty, dto.Price, currency));
        }

        TravelCheckoutResult result;
        try
        {
            result = await _cart.CheckoutAsync(
                items,
                new TravelGuest(request.Guest.Name.Trim(), request.Guest.Email.Trim()),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new CheckoutResponse(
            OrderRef: result.OrderRef,
            PaymentSessionId: result.PaymentSessionId,
            Amount: result.Amount,
            AmountFormatted: _priceFormatter.Format(result.Amount, result.Currency),
            Currency: result.Currency));
    }

    // ── 5. Confirm ─────────────────────────────────────────────────
    // POST /api/travel/confirm { orderRef }
    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm(
        [FromBody] ConfirmRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.OrderRef))
        {
            return BadRequest(new { error = "orderRef es requerido." });
        }

        TravelConfirmationResult result;
        try
        {
            result = await _cart.ConfirmAsync(request.OrderRef.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var items = result.Items.Select(i => new ConfirmItemDto(
            Product: i.Product.ToString(),
            OfferId: i.OfferId,
            Label: i.Label,
            ReservationId: i.ReservationId,
            Status: i.Status,
            Price: i.Price,
            PriceFormatted: _priceFormatter.Format(i.Price, i.Currency),
            Currency: i.Currency)).ToList();

        return Ok(new ConfirmResponse(
            Status: result.Status,
            ConfirmationCode: result.ConfirmationCode,
            OrderRef: result.OrderRef,
            Items: items));
    }

    // ── Helpers ────────────────────────────────────────────────────

    // El stub de hoteles solo necesita ocupación por habitación; sin edades reales
    // en la query, sembramos edades de niño (8) para satisfacer "niño con adulto".
    private static IReadOnlyList<int> BuildChildAges(int children)
        => children <= 0 ? Array.Empty<int>() : Enumerable.Repeat(8, children).ToList();

    // ── Request DTOs ───────────────────────────────────────────────

    public sealed record CartItemDto(string Product, string? OfferId, string? Label, decimal Price, string? Currency);
    public sealed record GuestDto(string Name, string Email);
    public sealed record CheckoutRequest(IReadOnlyList<CartItemDto> Items, GuestDto Guest);
    public sealed record ConfirmRequest(string OrderRef);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────

    public sealed record HotelOfferDto(
        string OfferId, string Product, string RoomTypeCode, string RoomTypeName, string RatePlanCode,
        string BoardBasis, bool Refundable, int RoomsLeft, decimal Price, string PriceFormatted, string Currency);

    public sealed record FlightOfferDto(
        string OfferId, string Product, string FlightId, string Origin, string Destination, int Stops,
        int TotalDurationMinutes, string Cabin, string FamilyName, bool Refundable, bool BaggageIncluded,
        decimal Price, string PriceFormatted, string Currency);

    public sealed record CarOfferDto(
        string OfferId, string Product, string CategoryName, string SippCode, string Provider,
        string Transmission, bool AirConditioning, int Days, string PricePerDayFormatted,
        decimal Price, string PriceFormatted, string Currency);

    public sealed record CheckoutResponse(
        string OrderRef, string PaymentSessionId, decimal Amount, string AmountFormatted, string Currency);

    public sealed record ConfirmItemDto(
        string Product, string OfferId, string Label, string ReservationId, string Status,
        decimal Price, string PriceFormatted, string Currency);

    public sealed record ConfirmResponse(
        string Status, string ConfirmationCode, string OrderRef, IReadOnlyList<ConfirmItemDto> Items);
}
