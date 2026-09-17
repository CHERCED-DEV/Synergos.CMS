using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;

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

    private readonly IMemberAccessGate _gate;

    public RealtyController(
        IPropertyCatalogProvider catalog,
        IVisitSchedulingService visits,
        IMortgageCalculator mortgage,
        ILeadCaptureService leads,
        IUserCollection collections,
        ISavedSearchService savedSearches,
        IPriceFormatter priceFormatter,
        IMemberAccessGate gate)
    {
        _catalog = catalog;
        _visits = visits;
        _mortgage = mortgage;
        _leads = leads;
        _collections = collections;
        _savedSearches = savedSearches;
        _gate = gate;
        _priceFormatter = priceFormatter;
    }


    // ── Identidad server-trusted y rol de agente (molde de Gov/Eventos/Blogs) ────
    //
    // Los favoritos y las búsquedas guardadas tomaban al usuario de un `?user=` o del
    // BODY: cualquiera leía y MUTABA las de otro. Y la consola del agente —con los leads,
    // que son nombres y teléfonos de personas reales— estaba abierta a cualquiera.

    /// <summary>Rol(es) con acceso a la consola del agente inmobiliario.</summary>
    private const string AgentRolesCsv = "agente,admin";

    /// <summary>Exige sesión; devuelve el correo server-trusted. 401 si es anónimo.</summary>
    private (IActionResult? denied, string userId) RequireUser()
    {
        var email = _gate.CurrentMemberEmail;
        if (!_gate.IsAuthenticated || string.IsNullOrWhiteSpace(email))
        {
            return (Unauthorized(new { error = "Se requiere iniciar sesión." }), string.Empty);
        }
        return (null, email);
    }

    /// <summary>
    /// Exige rol de AGENTE. 401 anónimo, 403 sin rol. Los leads son datos de contacto de
    /// personas: no basta con estar logueado.
    /// </summary>
    private (IActionResult? denied, string agentId) RequireAgent()
    {
        if (!_gate.IsAuthenticated)
        {
            return (Unauthorized(new { error = "Inicie sesión como agente." }), string.Empty);
        }
        if (!_gate.HasAnyRole(AgentRolesCsv))
        {
            // StatusCode(403) y NO Forbid(): con auth de members Forbid redirige al login.
            return (StatusCode(StatusCodes.Status403Forbidden, new { error = "Su cuenta no tiene permiso de agente." }), string.Empty);
        }
        return (null, _gate.CurrentMemberEmail ?? string.Empty);
    }

    // ── 1. Search facetado (home del dominio: lista + mapa) ─────────────
    // GET /api/realty/listings?q=&type=&minPrice=&maxPrice=&beds=&location=
    //   → { listings:[...], facets:[...] }
    // El cliente (`toSearchQuery`) manda NUEVE parámetros y la acción declaraba SEIS:
    // `operation`, `sort` y `bounds` entraban y se perdían en silencio — un [FromQuery]
    // ausente no falla, devuelve todo. Y `operation` viaja en TODAS las búsquedas: elegir
    // "Arriendo" seguía devolviendo apartamentos de venta a 850 millones.
    [HttpGet("listings")]
    public async Task<IActionResult> Listings(
        [FromQuery] string? q,
        [FromQuery] string? type,
        [FromQuery] decimal? minPrice,
        [FromQuery] decimal? maxPrice,
        [FromQuery] int? beds,
        [FromQuery] string? location,
        [FromQuery] string? operation,
        [FromQuery] string? sort,
        [FromQuery] string? bounds,
        CancellationToken cancellationToken)
    {
        var result = await _catalog.SearchAsync(
            // Vocabulario: la UI habla sale/rent y el dominio venta/arriendo. La traducción
            // de ENTRADA vive aquí, igual que la de salida (MapOperation).
            new PropertyQuery(q, type, minPrice, maxPrice, beds, location, MapOperationToDomain(operation)),
            cancellationToken);

        // El recorte por viewport ("buscar al mover el mapa") se aplica DESPUÉS de las
        // facetas a propósito: mover el mapa no debe reescribir los conteos de la columna
        // de filtros, que describen el universo buscado y no el trozo que se ve.
        var listings = ApplyBounds(result.Listings, bounds);

        return Ok(new ListingsResponse(
            Listings: SortListings(listings.Select(ToListingDto).ToList(), sort),
            Facets: result.Facets.Select(ToFacetDto).ToList(),
            // Contrato UI (SearchResult.total): cuántos coinciden. La UI caía a
            // `listings.length`, que es lo mismo hoy y deja de serlo el día que haya página.
            Total: listings.Count));
    }

    /// <summary>
    /// Recorta el resultado al rectángulo visible del mapa (<c>sur,oeste,norte,este</c>,
    /// tal como lo serializa el cliente). Un valor ilegible NO vacía la búsqueda: se ignora.
    /// </summary>
    /// <remarks>
    /// Un inmueble sin geocodificar (0,0) queda fuera de cualquier viewport real, que es lo
    /// correcto: no se puede pintar un pin donde no hay coordenada.
    /// </remarks>
    private static IReadOnlyList<PropertyListing> ApplyBounds(IReadOnlyList<PropertyListing> listings, string? bounds)
    {
        if (string.IsNullOrWhiteSpace(bounds))
        {
            return listings;
        }

        var parts = bounds.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            return listings;
        }

        var parsed = new double[4];
        for (var i = 0; i < 4; i++)
        {
            if (!double.TryParse(parts[i], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out parsed[i]))
            {
                return listings;
            }
        }

        var (south, west, north, east) = (parsed[0], parsed[1], parsed[2], parsed[3]);
        return listings
            .Where(l => l.Lat >= south && l.Lat <= north && l.Lng >= west && l.Lng <= east)
            .ToList();
    }

    /// <summary>
    /// Ordena según el vocabulario que declara la UI (<c>SortKey</c>: relevance |
    /// price-asc | price-desc | newest | area-desc), documentado allí como
    /// "Maps 1:1 to the API `sort` param".
    /// </summary>
    /// <remarks>
    /// <c>newest</c> NO se puede servir: <see cref="PropertyListing"/> no lleva fecha de
    /// publicación (la UI lee <c>publishedAt</c> y el borde no la tiene). Conserva el orden
    /// del proveedor en vez de inventar uno — degradar por AUSENCIA, nunca reordenar por un
    /// criterio que no es el pedido.
    /// </remarks>
    private static IReadOnlyList<ListingDto> SortListings(List<ListingDto> dtos, string? sort)
        => (sort ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "price-asc" => dtos.OrderBy(d => d.Price).ToList(),
            "price-desc" => dtos.OrderByDescending(d => d.Price).ToList(),
            "area-desc" => dtos.OrderByDescending(d => d.AreaM2).ToList(),
            _ => dtos,
        };

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
        if (request is null || string.IsNullOrWhiteSpace(request.ListingId))
        {
            return BadRequest(new { error = "listingId es requerido." });
        }
        if (request.Contact is null)
        {
            return BadRequest(new { error = "contact es requerido." });
        }

        var listingId = request.ListingId.Trim();
        var (slotId, slotError) = await ResolveSlotAsync(listingId, request.Slot, cancellationToken);
        if (slotId is null)
        {
            return BadRequest(new { error = slotError });
        }

        VisitResult result;
        try
        {
            result = await _visits.BookAsync(
                listingId,
                slotId,
                new VisitContact(request.Contact.Name ?? string.Empty, request.Contact.Email ?? string.Empty, request.Contact.Phone),
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

        var slot = await FindSlotAsync(listingId, slotId, cancellationToken);
        return Ok(new VisitResponse(new VisitDto(
            VisitId: result.VisitId,
            Status: result.Status,
            // Contrato UI (Visit): la confirmación lee `id`, `listingId` y `slot:{date,time}`.
            // Sin `id` su normalizador devolvía null y daba la cita por caída aunque el slot
            // hubiera quedado apartado de verdad. `mode` NO se emite: BookAsync no lo
            // guarda (VisitContact no lo lleva), y devolverlo diría que quedó registrado.
            Id: result.VisitId,
            ListingId: listingId,
            Slot: slot is null ? null : new VisitSlotDto(
                slot.StartUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                slot.StartUtc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture)))));
    }

    /// <summary>
    /// Resuelve el slot que la UI pide al id que la agenda del agente conoce.
    /// </summary>
    /// <remarks>
    /// <b>El cliente manda un OBJETO</b> —<c>slot: { date, time }</c>, porque elige una
    /// franja en un calendario— y este borde declaraba un <c>string</c>: System.Text.Json
    /// no puede meter un objeto en una cadena, así que la petición moría en el binding con
    /// un 400 ANTES de entrar al método. Agendar una visita fallaba el 100 % de las veces,
    /// y no se veía porque el cliente lo tapa con una cita inventada y un "visita
    /// confirmada" — alguien se presenta en la propiedad y no hay nadie.
    /// <para>Se aceptan las DOS formas: la cadena (el id de slot, consumers previos) y el
    /// objeto, que se casa contra <see cref="IVisitSchedulingService.GetSlotsAsync"/> por
    /// fecha y hora. Una franja que la agenda no tiene se RECHAZA con su motivo; inventarle
    /// un id sería agendar una visita a una hora en la que no atiende nadie.</para>
    /// </remarks>
    private async Task<(string? slotId, string? error)> ResolveSlotAsync(
        string listingId,
        System.Text.Json.JsonElement slot,
        CancellationToken cancellationToken)
    {
        if (slot.ValueKind == System.Text.Json.JsonValueKind.String)
        {
            var raw = slot.GetString();
            return string.IsNullOrWhiteSpace(raw)
                ? (null, "slot es requerido.")
                : (raw.Trim(), null);
        }

        if (slot.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return (null, "slot es requerido.");
        }

        var date = ReadSlotPart(slot, "date");
        var time = ReadSlotPart(slot, "time");
        if (string.IsNullOrWhiteSpace(date) || string.IsNullOrWhiteSpace(time))
        {
            return (null, "slot.date y slot.time son requeridos.");
        }

        var match = (await _visits.GetSlotsAsync(listingId, cancellationToken))
            .FirstOrDefault(s =>
                string.Equals(s.StartUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), date, StringComparison.Ordinal)
                && string.Equals(s.StartUtc.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture), time, StringComparison.Ordinal));

        return match is null
            ? (null, $"El agente no atiende visitas el {date} a las {time}.")
            : (match.Id, null);
    }

    private static string? ReadSlotPart(System.Text.Json.JsonElement slot, string name)
        => slot.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private async Task<Interfaces.VisitSlot?> FindSlotAsync(string listingId, string slotId, CancellationToken cancellationToken)
        => (await _visits.GetSlotsAsync(listingId, cancellationToken))
            .FirstOrDefault(s => string.Equals(s.Id, slotId, StringComparison.OrdinalIgnoreCase));

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
                new VisitContact(request.Contact.Name ?? string.Empty, request.Contact.Email ?? string.Empty, request.Contact.Phone),
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
    public async Task<IActionResult> Saved(CancellationToken cancellationToken)
    {
        var (denied, user) = RequireUser();
        if (denied is not null) { return denied; }
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
        // Identidad del GATE: se IGNORA el `User` del body, que permitía operar sobre
        // los favoritos y búsquedas de otro con solo poner su id.
        var (denied, actorId) = RequireUser();
        if (denied is not null) { return denied; }
        if (request is null)
        {
            return BadRequest(new { error = "user es requerido." });
        }

        var criteria = (request.Criteria ?? new SearchCriteria()).ToQuery();

        SavedSearch saved;
        try
        {
            saved = await _savedSearches.SaveAsync(actorId, criteria, request.Label, cancellationToken);
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
        var (denied, _) = RequireUser();
        if (denied is not null) { return denied; }
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
        // Identidad del GATE: se IGNORA el `User` del body, que permitía operar sobre
        // los favoritos y búsquedas de otro con solo poner su id.
        var (denied, actorId) = RequireUser();
        if (denied is not null) { return denied; }
        if (request is null || string.IsNullOrWhiteSpace(request.ListingId))
        {
            return BadRequest(new { error = "user y listingId son requeridos." });
        }

        await _collections.AddAsync(actorId, FavoritesCollection, request.ListingId.Trim(), cancellationToken);
        var favorites = await _collections.GetAsync(actorId, FavoritesCollection, cancellationToken);
        return Ok(new FavoritesResponse(favorites.Select(f => f.ItemRef).ToList()));
    }

    // DELETE /api/realty/favorite { user, listingId } → { favorites:[...] }
    [HttpDelete("favorite")]
    public async Task<IActionResult> RemoveFavorite([FromBody] FavoriteRequest? request, CancellationToken cancellationToken)
    {
        // Identidad del GATE: se IGNORA el `User` del body, que permitía operar sobre
        // los favoritos y búsquedas de otro con solo poner su id.
        var (denied, actorId) = RequireUser();
        if (denied is not null) { return denied; }
        if (request is null || string.IsNullOrWhiteSpace(request.ListingId))
        {
            return BadRequest(new { error = "user y listingId son requeridos." });
        }

        await _collections.RemoveAsync(actorId, FavoritesCollection, request.ListingId.Trim(), cancellationToken);
        var favorites = await _collections.GetAsync(actorId, FavoritesCollection, cancellationToken);
        return Ok(new FavoritesResponse(favorites.Select(f => f.ItemRef).ToList()));
    }

    // ── 10. Consola del agente: mini-CRM de leads (doc §6) ──────────────
    // GET /api/realty/agent/leads?agent= → { leads:[...] }
    [HttpGet("agent/leads")]
    public async Task<IActionResult> AgentLeads(CancellationToken cancellationToken)
    {
        // El agente ve SUS leads. Con `?agent=` un agente logueado leía la cartera de
        // otro — tener el rol no es tener derecho a los contactos del colega.
        var (denied, agent) = RequireAgent();
        if (denied is not null) { return denied; }
        if (string.IsNullOrWhiteSpace(agent))
        {
            return BadRequest(new { error = "El parámetro agent es requerido." });
        }

        var leads = await _leads.GetForAgentAsync(agent, cancellationToken);

        // El título del inmueble NO vive en AgentLead (solo su id) y la tarjeta del CRM lo
        // lee: sin él, TODAS las filas decían "Inmueble". Se resuelve una vez por inmueble
        // distinto contra el catálogo.
        var titles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var listingId in leads.Select(l => l.ListingId)
                     .Where(id => !string.IsNullOrWhiteSpace(id))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var listing = await _catalog.GetListingAsync(listingId, cancellationToken);
            if (listing is not null)
            {
                titles[listingId] = listing.Summary.Title;
            }
        }

        return Ok(new AgentLeadsResponse(
            leads.Select(l => ToAgentLeadDto(
                l,
                l.ListingId is not null && titles.TryGetValue(l.ListingId, out var title) ? title : string.Empty))
                .ToList()));
    }

    // POST /api/realty/lead/{id}/advance { status } → { leadId, status }
    [HttpPost("lead/{id}/advance")]
    public async Task<IActionResult> AdvanceLead(string id, [FromBody] AdvanceLeadRequest? request, CancellationToken cancellationToken)
    {
        var (denied, _) = RequireAgent();
        if (denied is not null) { return denied; }
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

        return Ok(new AdvanceLeadResponse(result.LeadId, MapLeadStatus(result.Status), result.Status.ToString()));
    }

    // ── 11. Publicar inmueble (agente, wizard SH-6 — doc §7) ────────────
    // POST /api/realty/listing { draft } → { listingId }
    [HttpPost("listing")]
    public async Task<IActionResult> PublishListing([FromBody] PublishListingRequest? request, CancellationToken cancellationToken)
    {
        // Publicar al catálogo era anónimo: cualquiera colgaba un inmueble en el sitio.
        var (denied, _) = RequireAgent();
        if (denied is not null) { return denied; }
        if (request is null)
        {
            return BadRequest(new { error = "El borrador del inmueble es requerido." });
        }

        PropertyDetail published;
        try
        {
            published = await _catalog.PublishListingAsync(
                request.ToDraft(MapOperationToDomain(request.Operation)),
                cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new PublishListingResponse(
            ListingId: published.Summary.Id,
            // Contrato UI (PublishListingResult): id | status. Sin `id` el normalizador del
            // wizard devolvía null y daba el POST por caído aunque el inmueble YA estuviera
            // publicado — el agente veía un id inventado y su inmueble sí existía.
            Id: published.Summary.Id,
            // Publicado = activo. `PropertyListing` no lleva estado de ciclo de vida
            // (reservado/vendido) todavía; emitir otra cosa sería inventarlo.
            Status: "active"));
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
        Subtitle: PropertyContentRules.BuildSubtitle(l),
        // Chips string[] que la UI lee como `listing.badges` (Destacado/Estrato/estado).
        Badges: PropertyContentRules.BuildBadges(l),
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

    /// <summary>
    /// Mapea la operación del vocabulario de la UI (<c>sale</c>/<c>rent</c>) al del dominio
    /// (<c>venta</c>/<c>arriendo</c>). Es la inversa de <see cref="MapOperation"/>, y hace
    /// falta en las DOS puertas de entrada: el filtro del search y el borrador que publica
    /// el agente. Sin ella, un inmueble publicado desde el wizard quedaba guardado como
    /// "sale" —fuera del vocabulario del catálogo— y ningún filtro de operación lo
    /// encontraba. Vacío o desconocido = sin traducir (no se inventa una operación).
    /// </summary>
    private static string? MapOperationToDomain(string? operation) =>
        (operation ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "sale" => "venta",
            "rent" => "arriendo",
            "" => null,
            _ => operation!.Trim().ToLowerInvariant(),
        };

    /// <summary>Mapea la operación es-CO al vocabulario que la UI espera (sale/rent).</summary>
    private static string MapOperation(string operation) =>
        (operation ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "venta" => "sale",
            "arriendo" => "rent",
            _ => operation ?? string.Empty
        };

    private static SavedSearchDto ToSavedSearchDto(SavedSearch s) => new(
        Id: s.Id,
        Label: s.Label,
        Criteria: SearchCriteria.From(s.Criteria),
        SavedAt: s.SavedAt,
        // Contrato UI (SavedSearch): `createdAt` (ISO corta) y `operation`. Sin `createdAt`
        // la tarjeta rellenaba con la fecha de HOY, así que toda búsqueda guardada decía
        // haberse guardado hoy. `savedAt` se conserva para consumers previos.
        CreatedAt: s.SavedAt.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        Operation: MapOperation(s.Criteria.Operation ?? string.Empty));

    private static AgentLeadDto ToAgentLeadDto(AgentLead l, string listingTitle = "") => new(
        LeadId: l.LeadId,
        AgentId: l.AgentId,
        ListingId: l.ListingId,
        ListingTitle: listingTitle,
        Name: l.Name,
        Email: l.Email,
        Phone: l.Phone,
        Message: l.Message,
        Status: MapLeadStatus(l.Status),
        StatusName: l.Status.ToString(),
        CreatedAt: l.CreatedAt);

    /// <summary>
    /// Traduce el estado del lead al vocabulario del tablero de la UI
    /// (<c>new|contacted|visit|won|lost</c>). El dominio lo nombra en es-CO y la UI no lo
    /// reconocía: su normalizador cae a <c>new</c> ante cualquier valor desconocido, así
    /// que TODO lead —incluidos los ya contactados— aparecía en la columna "Nuevo" y el
    /// agente los volvía a llamar.
    /// </summary>
    /// <remarks>
    /// <b><see cref="LeadStatus.Cerrado"/> no tiene traducción, y no se le inventa una.</b>
    /// El tablero distingue ganado de perdido y el dominio no: emitir <c>won</c> le pondría
    /// un resultado comercial que nadie registró. Sale como <c>closed</c>, que la UI aún no
    /// conoce — cerrar ese hueco es partir <c>Cerrado</c> en dos estados (decisión de
    /// negocio) o que el tablero acepte <c>closed</c>, y las dos cosas se deciden fuera de
    /// un mapper.
    /// </remarks>
    private static string MapLeadStatus(LeadStatus status) => status switch
    {
        LeadStatus.Nuevo => "new",
        LeadStatus.Contactado => "contacted",
        LeadStatus.Visita => "visit",
        LeadStatus.Cerrado => "closed",
        _ => "new",
    };

    // ── Request DTOs (binding del módulo Angular) ───────────────────────

    public sealed record ContactRequest(string? Name, string? Email, string? Phone);

    /// <summary>
    /// La solicitud de visita. <c>slot</c> se declara como <see cref="System.Text.Json.JsonElement"/>
    /// porque el contrato tiene DOS formas vivas: el objeto <c>{ date, time }</c> que manda la
    /// UI y la cadena con el id de slot de los consumers previos. Tipar una sola de las dos
    /// hace que la otra muera en el binding, con un 400 que el llamador no puede distinguir
    /// de "el servidor no está".
    /// </summary>
    public sealed record VisitRequest(
        string? ListingId,
        System.Text.Json.JsonElement Slot,
        ContactRequest? Contact,
        string? Mode = null);

    public sealed record MortgageRequest(decimal Price, decimal DownPayment, int TermMonths, decimal AnnualRate);

    public sealed record LeadRequest(string? ListingId, ContactRequest? Contact, string? Message);

    /// <summary>Criterios de búsqueda (espejo JSON de <see cref="PropertyQuery"/>).</summary>
    /// <summary>
    /// Criterios de búsqueda (espejo JSON de <see cref="PropertyQuery"/>).
    /// </summary>
    /// <remarks>
    /// <b>El texto llegaba por <c>q</c> y este record solo declaraba <c>text</c></b>, así
    /// que guardar "apartamentos en Chicó" guardaba una búsqueda SIN texto: re-ejecutarla
    /// devolvía el catálogo entero y la alerta avisaba de inmuebles que nadie pidió. Se
    /// emiten y se aceptan las dos claves; <c>q</c> gana cuando viene.
    /// </remarks>
    public sealed record SearchCriteria(
        string? Text = null,
        string? Q = null,
        string? Type = null,
        decimal? MinPrice = null,
        decimal? MaxPrice = null,
        int? Beds = null,
        string? Location = null,
        string? Operation = null)
    {
        private string? Termino => string.IsNullOrWhiteSpace(Q) ? Text : Q;

        public PropertyQuery ToQuery() =>
            new(Termino, Type, MinPrice, MaxPrice, Beds, Location, MapOperationToDomain(Operation));

        public static SearchCriteria From(PropertyQuery q) =>
            new(q.Text, q.Text, q.Type, q.MinPrice, q.MaxPrice, q.Beds, q.Location, MapOperation(q.Operation ?? string.Empty));
    }

    /// <summary>
    /// El <c>User</c> es NULABLE a propósito, y ya no decide nada: la identidad sale del
    /// gate desde el barrido T2. Se conserva el campo solo por compatibilidad con clientes
    /// viejos que aún lo manden — el controller lo IGNORA.
    ///
    /// Que sea nulable no es cosmético. Con <c>[ApiController]</c> y tipos de referencia no
    /// anulables, un <c>string</c> obligatorio hace que la validación automática rechace con
    /// 400 cualquier cuerpo que no lo traiga… y el cliente correcto —el que dejó de mandar
    /// la identidad justamente para no revivir el IDOR— es exactamente ese. Medido en vivo:
    /// POST y DELETE de favorito devolvían 400 con el cuerpo bien formado, así que la
    /// función entera estaba rota mientras el build y 31 tests seguían verdes.
    /// </summary>
    public sealed record SaveSearchRequest(string? User, SearchCriteria? Criteria, string? Label);

    /// <summary>Mismo caso que <see cref="SaveSearchRequest"/>: <c>User</c> nulable e ignorado.</summary>
    public sealed record FavoriteRequest(string? User, string ListingId);

    public sealed record AdvanceLeadRequest(string Status);

    public sealed record GeoRequest(double Lat, double Lng);

    /// <summary>
    /// El borrador del wizard SH-6.
    /// </summary>
    /// <remarks>
    /// <b>El pin del mapa llegaba PLANO y se tiraba.</b> El paso de ubicación manda
    /// <c>lat</c> y <c>lng</c> en la raíz; este record solo declaraba <c>geo:{lat,lng}</c>,
    /// así que <c>ToDraft</c> publicaba en <c>(0, 0)</c> — la isla Null, frente a África.
    /// El inmueble quedaba publicado, buscable y <b>sin pin en el mapa</b>, sin que nada
    /// fallara: exactamente el modo de fallo que <c>CLAUDE.md</c> nombra para este vertical.
    /// <para>Igual con el área: la UI manda <c>areaBuilt</c> (área construida, la cifra
    /// grande de la tarjeta) y el record solo tenía <c>area</c> → todo inmueble publicado
    /// salía con 0 m².</para>
    /// <para>Las claves nuevas GANAN sobre las anidadas/legacy solo cuando vienen; ninguna
    /// se quita.</para>
    /// </remarks>
    public sealed record PublishListingRequest(
        string? Title = null,
        string? Type = null,
        string? Operation = null,
        decimal Price = 0m,
        int Beds = 0,
        int Baths = 0,
        int Area = 0,
        int AreaBuilt = 0,
        string? City = null,
        GeoRequest? Geo = null,
        double? Lat = null,
        double? Lng = null,
        IReadOnlyList<string>? Gallery = null,
        string? Description = null,
        string? Neighborhood = null,
        int Stratum = 0,
        string? Currency = null,
        string? AgentName = null,
        string? AgentPhone = null,
        // La dirección que escribe quien publica. La ficha la pinta (`LocationDto.Address`) y
        // este record no la recibía: el inmueble salía con barrio y ciudad y sin calle (#110).
        string? Address = null)
    {
        /// <summary>El área construida, venga plana (<c>areaBuilt</c>) o legacy (<c>area</c>).</summary>
        public int BuiltArea => AreaBuilt > 0 ? AreaBuilt : Area;

        /// <summary>El pin, venga plano (<c>lat</c>/<c>lng</c>) o anidado (<c>geo</c>).</summary>
        public PropertyGeo Pin => new(Lat ?? Geo?.Lat ?? 0d, Lng ?? Geo?.Lng ?? 0d);

        public PropertyDraft ToDraft(string? operation = null) => new(
            Title: Title ?? string.Empty,
            Type: Type ?? string.Empty,
            // La operación se guarda en el vocabulario del DOMINIO: el wizard manda
            // sale/rent y guardarlo crudo dejaba el inmueble fuera de todo filtro.
            Operation: operation ?? Operation ?? string.Empty,
            Price: Price,
            Beds: Beds,
            Baths: Baths,
            Area: BuiltArea,
            City: City ?? string.Empty,
            Geo: Pin,
            Gallery: Gallery ?? Array.Empty<string>(),
            Description: Description ?? string.Empty,
            Neighborhood: Neighborhood,
            Stratum: Stratum,
            Currency: Currency,
            AgentName: AgentName,
            AgentPhone: AgentPhone,
            Address: Address);
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
        IReadOnlyList<FacetDto> Facets,
        int Total);

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

    public sealed record VisitSlotDto(string Date, string Time);

    // Contrato UI (Visit): id | listingId | slot{date,time} | status. `visitId` se conserva
    // para consumers previos y porta el mismo id.
    public sealed record VisitDto(
        string VisitId,
        string Status,
        string Id,
        string ListingId,
        VisitSlotDto? Slot);

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
        DateTimeOffset SavedAt,
        string CreatedAt,
        string Operation);

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
        // Contrato UI (AgentLead.listingTitle): resuelto contra el catálogo; vacío cuando
        // el inmueble ya no está.
        string ListingTitle,
        string Name,
        string Email,
        string? Phone,
        string Message,
        // Vocabulario de la UI (new|contacted|visit|closed). `statusName` conserva el
        // nombre es-CO del dominio para consumers previos.
        string Status,
        string StatusName,
        DateTimeOffset CreatedAt);

    public sealed record AgentLeadsResponse(IReadOnlyList<AgentLeadDto> Leads);

    public sealed record AdvanceLeadResponse(string LeadId, string Status, string StatusName);

    public sealed record PublishListingResponse(string ListingId, string Id, string Status);
}
