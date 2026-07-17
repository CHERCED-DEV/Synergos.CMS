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
    private const string FavoritesCollection = "favorites";

    private readonly IPropertyCatalogProvider _catalog;
    private readonly IVisitSchedulingService _visits;
    private readonly IMortgageCalculator _mortgage;
    private readonly ILeadCaptureService _leads;
    private readonly IUserCollection _collections;
    private readonly ISavedSearchService _savedSearches;
    private readonly IPriceFormatter _priceFormatter;

    public RealtyController(
        IPropertyCatalogProvider catalog,
        IVisitSchedulingService visits,
        IMortgageCalculator mortgage,
        ILeadCaptureService leads,
        IUserCollection collections,
        ISavedSearchService savedSearches,
        IPriceFormatter priceFormatter)
    {
        _catalog = catalog;
        _visits = visits;
        _mortgage = mortgage;
        _leads = leads;
        _collections = collections;
        _savedSearches = savedSearches;
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
            // PDP specs: la UI (normalizeSpecs) lee `specs` como OBJETO numérico, no
            // como array {label,value}. El array humano se conserva en `specList`.
            Specs: new SpecsDto(
                detail.Summary.Beds, detail.Summary.Baths, detail.Summary.AreaM2,
                0, 0, detail.Summary.Stratum, 0, 0),
            SpecList: detail.Specs.Select(s => new SpecDto(s.Label, s.Value)).ToList(),
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

    // ── 6. Cuenta: favoritos + búsquedas guardadas (doc §4) ─────────────
    // GET /api/realty/saved?user= → { favorites:[...], searches:[...] }
    [HttpGet("saved")]
    public async Task<IActionResult> Saved([FromQuery] string? user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user))
        {
            return BadRequest(new { error = "El parámetro user es requerido." });
        }

        var owner = user.Trim();
        var favorites = await _collections.GetAsync(owner, FavoritesCollection, cancellationToken);
        var searches = await _savedSearches.GetForOwnerAsync(owner, cancellationToken);

        return Ok(new SavedResponse(
            Favorites: favorites.Select(f => f.ItemRef).ToList(),
            Searches: searches.Select(ToSavedSearchDto).ToList()));
    }

    // ── 7. Guardar una búsqueda (con criterios) ─────────────────────────
    // POST /api/realty/saved-search { user, criteria } → { id, label, criteria }
    [HttpPost("saved-search")]
    public async Task<IActionResult> SaveSearch([FromBody] SaveSearchRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.User))
        {
            return BadRequest(new { error = "user es requerido." });
        }

        var criteria = (request.Criteria ?? new SearchCriteria()).ToQuery();

        SavedSearch saved;
        try
        {
            saved = await _savedSearches.SaveAsync(request.User.Trim(), criteria, request.Label, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(ToSavedSearchDto(saved));
    }

    // ── 8. Alertas: nuevos matches de una búsqueda guardada ─────────────
    // GET /api/realty/saved/{id}/matches → { count, listings:[...] }
    [HttpGet("saved/{id}/matches")]
    public async Task<IActionResult> SavedMatches(string id, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id de la búsqueda es requerido." });
        }

        SavedSearchMatches matches;
        try
        {
            matches = await _savedSearches.GetMatchesAsync(id.Trim(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        return Ok(new SavedMatchesResponse(
            Count: matches.Count,
            Listings: matches.Listings.Select(ToListingDto).ToList()));
    }

    // ── 9. Favoritos: marcar / desmarcar un inmueble ────────────────────
    // POST /api/realty/favorite { user, listingId } → { favorites:[...] }
    [HttpPost("favorite")]
    public async Task<IActionResult> AddFavorite([FromBody] FavoriteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.User) || string.IsNullOrWhiteSpace(request.ListingId))
        {
            return BadRequest(new { error = "user y listingId son requeridos." });
        }

        await _collections.AddAsync(request.User.Trim(), FavoritesCollection, request.ListingId.Trim(), cancellationToken);
        var favorites = await _collections.GetAsync(request.User.Trim(), FavoritesCollection, cancellationToken);
        return Ok(new FavoritesResponse(favorites.Select(f => f.ItemRef).ToList()));
    }

    // DELETE /api/realty/favorite { user, listingId } → { favorites:[...] }
    [HttpDelete("favorite")]
    public async Task<IActionResult> RemoveFavorite([FromBody] FavoriteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.User) || string.IsNullOrWhiteSpace(request.ListingId))
        {
            return BadRequest(new { error = "user y listingId son requeridos." });
        }

        await _collections.RemoveAsync(request.User.Trim(), FavoritesCollection, request.ListingId.Trim(), cancellationToken);
        var favorites = await _collections.GetAsync(request.User.Trim(), FavoritesCollection, cancellationToken);
        return Ok(new FavoritesResponse(favorites.Select(f => f.ItemRef).ToList()));
    }

    // ── 10. Consola del agente: mini-CRM de leads (doc §6) ──────────────
    // GET /api/realty/agent/leads?agent= → { leads:[...] }
    [HttpGet("agent/leads")]
    public async Task<IActionResult> AgentLeads([FromQuery] string? agent, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(agent))
        {
            return BadRequest(new { error = "El parámetro agent es requerido." });
        }

        var leads = await _leads.GetForAgentAsync(agent.Trim(), cancellationToken);
        return Ok(new AgentLeadsResponse(leads.Select(ToAgentLeadDto).ToList()));
    }

    // POST /api/realty/lead/{id}/advance { status } → { leadId, status }
    [HttpPost("lead/{id}/advance")]
    public async Task<IActionResult> AdvanceLead(string id, [FromBody] AdvanceLeadRequest? request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(new { error = "El id del lead es requerido." });
        }
        if (request is null || string.IsNullOrWhiteSpace(request.Status)
            || !Enum.TryParse<LeadStatus>(request.Status.Trim(), ignoreCase: true, out var status))
        {
            return BadRequest(new { error = "status es requerido y debe ser Nuevo|Contactado|Visita|Cerrado." });
        }

        LeadAdvanceResult result;
        try
        {
            result = await _leads.AdvanceLeadAsync(id.Trim(), status, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        return Ok(new AdvanceLeadResponse(result.LeadId, result.Status.ToString()));
    }

    // ── 11. Publicar inmueble (agente, wizard SH-6 — doc §7) ────────────
    // POST /api/realty/listing { draft } → { listingId }
    [HttpPost("listing")]
    public async Task<IActionResult> PublishListing([FromBody] PublishListingRequest? request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "El borrador del inmueble es requerido." });
        }

        PropertyDetail published;
        try
        {
            published = await _catalog.PublishListingAsync(request.ToDraft(), cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new PublishListingResponse(published.Summary.Id));
    }

    // ── Mappers a DTOs JSON estables ────────────────────────────────────

    private ListingDto ToListingDto(PropertyListing l) => new(
        Id: l.Id,
        Slug: l.Slug,
        Title: l.Title,
        // Vocabulario UI: la UI espera 'sale'/'rent' (no 'venta'/'arriendo').
        Operation: MapOperation(l.Operation),
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
        // Geo anidado: la UI lee `listing.geo.{neighborhood,city,lat,lng}` (card
        // subtítulo + marcadores del mapa). Sin esto quedaba " · " y mapa sin pines.
        Geo: new LocationDto(l.Lat, l.Lng, string.Empty, l.Neighborhood, l.City),
        // Specs anidado: la UI lee `listing.specs.{beds,baths,areaBuilt,stratum}`
        // (card + comparador). Los campos sin fuente en el resumen van en 0.
        Specs: new SpecsDto(l.Beds, l.Baths, l.AreaM2, 0, 0, l.Stratum, 0, 0),
        // Subtítulo de la card (línea de specs) que la UI lee como `listing.subtitle`.
        Subtitle: BuildSubtitle(l),
        // Chips string[] que la UI lee como `listing.badges` (Destacado/Estrato/estado).
        Badges: BuildBadges(l),
        ImageUrl: l.ImageUrl,
        Cover: l.ImageUrl,
        Featured: l.Featured);

    private static FacetDto ToFacetDto(PropertyFacet f) => new(
        Name: f.Name,
        // La UI lee `facets[].key` + `values[].label`; se conservan Name/Value legacy.
        Key: f.Name,
        Values: f.Values.Select(v => new FacetValueDto(v.Value, v.Label ?? v.Value, v.Count)).ToList(),
        // La UI lee `facets[].kind` (realty-api.client.ts normalizeFacets) para saber si la
        // faceta es de valor único. Sin esto, `beds` llegaba sin kind → checkbox → el usuario
        // marcaba "4+ habitaciones" y "1+ habitación" y veía el catálogo entero.
        Kind: f.Kind);

    // ── Helpers de reshape (vocabulario + derivaciones para la UI) ──────

    /// <summary>Mapea la operación es-CO al vocabulario que la UI espera (sale/rent).</summary>
    private static string MapOperation(string operation) =>
        (operation ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "venta" => "sale",
            "arriendo" => "rent",
            _ => operation ?? string.Empty
        };

    /// <summary>Línea de specs de la card (`listing.subtitle`) desde beds/baths/área/estrato.</summary>
    private static string BuildSubtitle(PropertyListing l)
    {
        var parts = new List<string>(4);
        if (l.Beds > 0) parts.Add($"{l.Beds} hab");
        if (l.Baths > 0) parts.Add($"{l.Baths} {(l.Baths == 1 ? "baño" : "baños")}");
        if (l.AreaM2 > 0) parts.Add($"{l.AreaM2} m²");
        if (l.Stratum > 0) parts.Add($"Estrato {l.Stratum}");
        return string.Join(" · ", parts);
    }

    /// <summary>Chips derivados (`listing.badges`) desde Featured/Estrato/operación.</summary>
    private static IReadOnlyList<string> BuildBadges(PropertyListing l)
    {
        var badges = new List<string>(3);
        if (l.Featured) badges.Add("Destacado");
        if (l.Stratum > 0) badges.Add($"Estrato {l.Stratum}");
        var status = (l.Operation ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "venta" => "En venta",
            "arriendo" => "En arriendo",
            _ => null
        };
        if (status is not null) badges.Add(status);
        return badges;
    }

    private static SavedSearchDto ToSavedSearchDto(SavedSearch s) => new(
        Id: s.Id,
        Label: s.Label,
        Criteria: SearchCriteria.From(s.Criteria),
        SavedAt: s.SavedAt);

    private static AgentLeadDto ToAgentLeadDto(AgentLead l) => new(
        LeadId: l.LeadId,
        AgentId: l.AgentId,
        ListingId: l.ListingId,
        Name: l.Name,
        Email: l.Email,
        Phone: l.Phone,
        Message: l.Message,
        Status: l.Status.ToString(),
        CreatedAt: l.CreatedAt);

    // ── Request DTOs (binding del módulo Angular) ───────────────────────

    public sealed record ContactRequest(string Name, string Email, string? Phone);

    public sealed record VisitRequest(string ListingId, string Slot, ContactRequest Contact);

    public sealed record MortgageRequest(decimal Price, decimal DownPayment, int TermMonths, decimal AnnualRate);

    public sealed record LeadRequest(string ListingId, ContactRequest Contact, string? Message);

    /// <summary>Criterios de búsqueda (espejo JSON de <see cref="PropertyQuery"/>).</summary>
    public sealed record SearchCriteria(
        string? Text = null,
        string? Type = null,
        decimal? MinPrice = null,
        decimal? MaxPrice = null,
        int? Beds = null,
        string? Location = null)
    {
        public PropertyQuery ToQuery() => new(Text, Type, MinPrice, MaxPrice, Beds, Location);

        public static SearchCriteria From(PropertyQuery q) =>
            new(q.Text, q.Type, q.MinPrice, q.MaxPrice, q.Beds, q.Location);
    }

    public sealed record SaveSearchRequest(string User, SearchCriteria? Criteria, string? Label);

    public sealed record FavoriteRequest(string User, string ListingId);

    public sealed record AdvanceLeadRequest(string Status);

    public sealed record GeoRequest(double Lat, double Lng);

    public sealed record PublishListingRequest(
        string? Title,
        string? Type,
        string? Operation,
        decimal Price,
        int Beds,
        int Baths,
        int Area,
        string? City,
        GeoRequest? Geo,
        IReadOnlyList<string>? Gallery,
        string? Description,
        string? Neighborhood,
        int Stratum,
        string? Currency,
        string? AgentName,
        string? AgentPhone)
    {
        public PropertyDraft ToDraft() => new(
            Title: Title ?? string.Empty,
            Type: Type ?? string.Empty,
            Operation: Operation ?? string.Empty,
            Price: Price,
            Beds: Beds,
            Baths: Baths,
            Area: Area,
            City: City ?? string.Empty,
            Geo: new PropertyGeo(Geo?.Lat ?? 0d, Geo?.Lng ?? 0d),
            Gallery: Gallery ?? Array.Empty<string>(),
            Description: Description ?? string.Empty,
            Neighborhood: Neighborhood,
            Stratum: Stratum,
            Currency: Currency,
            AgentName: AgentName,
            AgentPhone: AgentPhone);
    }

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
        LocationDto Geo,
        SpecsDto Specs,
        string Subtitle,
        IReadOnlyList<string> Badges,
        string ImageUrl,
        string Cover,
        bool Featured);

    public sealed record FacetValueDto(string Value, string Label, int Count);

    public sealed record FacetDto(
        string Name,
        string Key,
        IReadOnlyList<FacetValueDto> Values,
        string Kind = "MultiSelect");

    public sealed record ListingsResponse(
        IReadOnlyList<ListingDto> Listings,
        IReadOnlyList<FacetDto> Facets);

    public sealed record SpecDto(string Label, string Value);

    /// <summary>
    /// Specs numéricos anidados que la UI espera (normalizeSpecs → objeto, no array):
    /// `listing.specs` en card/comparador y `specs` en la PDP. Los campos sin fuente
    /// en el resumen (areaPrivate/parking/ageYears/floor) van en 0.
    /// </summary>
    public sealed record SpecsDto(
        int Beds,
        int Baths,
        int AreaBuilt,
        int AreaPrivate,
        int Parking,
        int Stratum,
        int AgeYears,
        int Floor);

    public sealed record LocationDto(double Lat, double Lng, string Address, string Neighborhood, string City);

    // Agency/Rating: la UI lee `agent.agency` + `agent.rating`. PropertyDetail solo
    // expone AgentName/AgentPhone → sin fuente; se emiten como null (no se inventan).
    public sealed record AgentDto(string Name, string Phone, string? Agency = null, double? Rating = null);

    public sealed record ListingDetailResponse(
        ListingDto Listing,
        SpecsDto Specs,
        IReadOnlyList<SpecDto> SpecList,
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

    public sealed record SavedSearchDto(
        string Id,
        string Label,
        SearchCriteria Criteria,
        DateTimeOffset SavedAt);

    public sealed record SavedResponse(
        IReadOnlyList<string> Favorites,
        IReadOnlyList<SavedSearchDto> Searches);

    public sealed record SavedMatchesResponse(
        int Count,
        IReadOnlyList<ListingDto> Listings);

    public sealed record FavoritesResponse(IReadOnlyList<string> Favorites);

    public sealed record AgentLeadDto(
        string LeadId,
        string AgentId,
        string ListingId,
        string Name,
        string Email,
        string? Phone,
        string Message,
        string Status,
        DateTimeOffset CreatedAt);

    public sealed record AgentLeadsResponse(IReadOnlyList<AgentLeadDto> Leads);

    public sealed record AdvanceLeadResponse(string LeadId, string Status);

    public sealed record PublishListingResponse(string ListingId);
}
