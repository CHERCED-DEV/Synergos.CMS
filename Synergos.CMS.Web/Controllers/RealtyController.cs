using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// API JSON del vertical <strong>Propiedades</strong> (OLA 7 — portal inmobiliario,
/// doc propiedades-app-spec). La consume el módulo Angular <c>module-realty-portal</c>:
/// entrar al dominio = caer directo en la app real (search lista↔mapa → ficha →
/// agendar visita / contactar agente, + calculadora de hipoteca).
/// </summary>
/// <remarks>
/// La capa Web SOLO orquesta y mapea a DTOs JSON estables — toda la lógica vive en
/// los seams (Application, sin Umbraco — ADR 0002), reusando el MOTOR:
/// <list type="bullet">
/// <item><see cref="IPropertyCatalogProvider"/> — search facetado + ficha (specs +
///   galería + ubicación geo).</item>
/// <item><see cref="IVisitSchedulingService"/> — agendar visita: aparta el slot vía
///   <see cref="IReservationService.HoldItemAsync"/> + <see cref="IReservationService.ConfirmAsync"/>
///   <strong>SIN pago</strong> (la visita es gratis). La visita = recurso reservable
///   polimórfico (igual que habitación/asiento/médico).</item>
/// <item><see cref="IMortgageCalculator"/> — cálculo puro/determinista (amortización
///   francesa).</item>
/// <item><see cref="ILeadCaptureService"/> — captura de lead (contactar agente).</item>
/// </list>
/// El precio se formatea es-CO vía <see cref="IPriceFormatter"/>. Contrato (lo
/// programa el agente UI): <c>GET listings · GET listing/{id} · POST visit ·
/// POST mortgage · POST lead</c>.
/// </remarks>
[ApiController]
[Route("api/realty")]
public sealed class RealtyController : ControllerBase
{
    private readonly IPropertyCatalogProvider _catalog;
    private readonly IVisitSchedulingService _visits;
    private readonly IMortgageCalculator _mortgage;
    private readonly ILeadCaptureService _leads;
    private readonly IPriceFormatter _priceFormatter;

    public RealtyController(
        IPropertyCatalogProvider catalog,
        IVisitSchedulingService visits,
        IMortgageCalculator mortgage,
        ILeadCaptureService leads,
        IPriceFormatter priceFormatter)
    {
        _catalog = catalog;
        _visits = visits;
        _mortgage = mortgage;
        _leads = leads;
        _priceFormatter = priceFormatter;
    }

    // ── 1. Search facetado (home del dominio: lista + mapa) ─────────────
    // GET /api/realty/listings?q=&type=&minPrice=&maxPrice=&beds=&location=
    //   → { listings:[...], facets:[...] }
    [HttpGet("listings")]
    public async Task<IActionResult> Listings(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int? beds,
        [FromQuery] string? location,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.SearchAsync(
            new PropertyQuery(q, type, minPrice, maxPrice, beds, location),
            cancellationToken);

        return Ok(new ListingsResponse(
            Listings: result.Listings.Select(ToListingDto).ToList(),
            Facets: result.Facets.Select(ToFacetDto).ToList()));
    }

    // ── 2. Ficha de propiedad (PDP) ─────────────────────────────────────
    // GET /api/realty/listing/{id} → { listing, specs, gallery:[...], location }
    [HttpGet("listing/{id}")]
    public async Task<IActionResult> Listing(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del listado es requerido." });
        }

        var detail = await _catalog.GetListingAsync(id, cancellationToken);
        if (detail is null)
        {
            return NotFound(new { error = $"Listado '{id}' no encontrado." });
        }

        return Ok(new ListingDetailResponse(
            Listing: ToListingDto(detail.Summary),
            Specs: detail.Specs.Select(s => new SpecDto(s.Label, s.Value)).ToList(),
            Amenities: detail.Amenities,
            Gallery: detail.Gallery,
            Description: detail.Description,
            Location: new LocationDto(
                detail.Location.Lat, detail.Location.Lng,
                detail.Location.Address, detail.Location.Neighborhood, detail.Location.City),
            Agent: new AgentDto(detail.AgentName, detail.AgentPhone)));
    }

    // ── 3. Agendar visita (reusa el motor, SIN pago) ────────────────────
    // POST /api/realty/visit { listingId, slot, contact } → { visit }
    [HttpPost("visit")]
    public async Task<IActionResult> Visit([FromBody] VisitRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ListingId) || string.IsNullOrWhiteSpace(request.Slot))
        {
            return BadRequest(new { error = "listingId y slot son requeridos." });
        }
        if (request.Contact is null)
        {
            return BadRequest(new { error = "contact es requerido." });
        }

        VisitResult result;
        try
        {
            result = await _visits.BookAsync(
                request.ListingId.Trim(),
                request.Slot.Trim(),
                new VisitContact(request.Contact.Name, request.Contact.Email, request.Contact.Phone),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }

        return Ok(new VisitResponse(new VisitDto(result.VisitId, result.Status)));
    }

    // ── 4. Calculadora de hipoteca (puro/determinista) ──────────────────
    // POST /api/realty/mortgage { price, downPayment, termMonths, annualRate }
    //   → { monthly, totalInterest, totalPaid, schedule? }
    [HttpPost("mortgage")]
    public IActionResult Mortgage([FromBody] MortgageRequest? request)
    {
        if (request is null)
        {
            return BadRequest(new { error = "El cuerpo de la solicitud es requerido." });
        }

        MortgageResult result;
        try
        {
            result = _mortgage.Calculate(request.Price, request.DownPayment, request.TermMonths, request.AnnualRate);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new MortgageResponse(
            Monthly: result.Monthly,
            MonthlyFormatted: _priceFormatter.Format(result.Monthly, "COP"),
            TotalInterest: result.TotalInterest,
            TotalInterestFormatted: _priceFormatter.Format(result.TotalInterest, "COP"),
            TotalPaid: result.TotalPaid,
            TotalPaidFormatted: _priceFormatter.Format(result.TotalPaid, "COP"),
            Schedule: result.Schedule
                .Select(r => new MortgageScheduleDto(r.Period, r.Payment, r.Interest, r.Principal, r.Balance))
                .ToList()));
    }

    // ── 5. Contactar agente / lead ──────────────────────────────────────
    // POST /api/realty/lead { listingId, contact, message } → { leadId }
    [HttpPost("lead")]
    public async Task<IActionResult> Lead([FromBody] LeadRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.ListingId))
        {
            return BadRequest(new { error = "listingId es requerido." });
        }
        if (request.Contact is null)
        {
            return BadRequest(new { error = "contact es requerido." });
        }

        LeadResult result;
        try
        {
            result = await _leads.CaptureAsync(
                request.ListingId.Trim(),
                new VisitContact(request.Contact.Name, request.Contact.Email, request.Contact.Phone),
                request.Message ?? string.Empty,
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new LeadResponse(result.LeadId));
    }

    // ── Mappers a DTOs JSON estables ────────────────────────────────────

    private ListingDto ToListingDto(PropertyListing l) => new(
        Id: l.Id,
        Slug: l.Slug,
        Title: l.Title,
        Operation: l.Operation,
        Type: l.Type,
        Price: l.Price,
        PriceFormatted: _priceFormatter.Format(l.Price, l.Currency),
        Currency: l.Currency,
        City: l.City,
        Neighborhood: l.Neighborhood,
        Beds: l.Beds,
        Baths: l.Baths,
        AreaM2: l.AreaM2,
        Stratum: l.Stratum,
        Lat: l.Lat,
        Lng: l.Lng,
        ImageUrl: l.ImageUrl,
        Featured: l.Featured);

    private static FacetDto ToFacetDto(PropertyFacet f) => new(
        Name: f.Name,
        Values: f.Values.Select(v => new FacetValueDto(v.Value, v.Count)).ToList());

    // ── Request DTOs (binding del módulo Angular) ───────────────────────

    public sealed record ContactRequest(string Name, string Email, string? Phone);

    public sealed record VisitRequest(string ListingId, string Slot, ContactRequest Contact);

    public sealed record MortgageRequest(decimal Price, decimal DownPayment, int TermMonths, decimal AnnualRate);

    public sealed record LeadRequest(string ListingId, ContactRequest Contact, string? Message);

    // ── Response DTOs (JSON estable para la UI) ─────────────────────────

    public sealed record ListingDto(
        string Id,
        string Slug,
        string Title,
        string Operation,
        string Type,
        decimal Price,
        string PriceFormatted,
        string Currency,
        string City,
        string Neighborhood,
        int Beds,
        int Baths,
        int AreaM2,
        int Stratum,
        double Lat,
        double Lng,
        string ImageUrl,
        bool Featured);

    public sealed record FacetValueDto(string Value, int Count);

    public sealed record FacetDto(string Name, IReadOnlyList<FacetValueDto> Values);

    public sealed record ListingsResponse(
        IReadOnlyList<ListingDto> Listings,
        IReadOnlyList<FacetDto> Facets);

    public sealed record SpecDto(string Label, string Value);

    public sealed record LocationDto(double Lat, double Lng, string Address, string Neighborhood, string City);

    public sealed record AgentDto(string Name, string Phone);

    public sealed record ListingDetailResponse(
        ListingDto Listing,
        IReadOnlyList<SpecDto> Specs,
        IReadOnlyList<string> Amenities,
        IReadOnlyList<string> Gallery,
        string Description,
        LocationDto Location,
        AgentDto Agent);

    public sealed record VisitDto(string VisitId, string Status);

    public sealed record VisitResponse(VisitDto Visit);

    public sealed record MortgageScheduleDto(
        int Period,
        decimal Payment,
        decimal Interest,
        decimal Principal,
        decimal Balance);

    public sealed record MortgageResponse(
        decimal Monthly,
        string MonthlyFormatted,
        decimal TotalInterest,
        string TotalInterestFormatted,
        decimal TotalPaid,
        string TotalPaidFormatted,
        IReadOnlyList<MortgageScheduleDto> Schedule);

    public sealed record LeadResponse(string LeadId);
}
