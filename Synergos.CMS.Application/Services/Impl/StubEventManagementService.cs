using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IEventManagementService"/> — cara de organizador (operacional)
/// del vertical Eventos (doc eventos-app-spec §2 cara B). Lee los tickets confirmados
/// del registro de entradas (<see cref="EventTicketLedger"/>) y el aforo del
/// catálogo (<see cref="IEventCatalogProvider"/>) para armar el dashboard de
/// asistentes/aforo/vendidos, y delega el check-in (validación + marca idempotente)
/// al registro — fuente de verdad de las entradas emitidas.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica el estado de los tickets: lo COMPONE
/// del <see cref="EventTicketLedger"/>.
/// <b>Y cuelga del REGISTRO, no del motor de compra</b> (HU #35, rebanada 2b): antes
/// colgaba del motor CONCRETO, así que cambiar por dónde se compra —el orquestador en
/// vez del motor en proceso— habría dejado esta cara leyendo un almacén vacío. Las
/// entradas existirían, el escáner diría <c>invalid</c>, y nada en el build avisaría.
/// La puerta no depende de por dónde se pagó. <see cref="CheckInAsync"/>
/// es IDEMPOTENTE — el primer check-in devuelve <c>valid</c>, los siguientes
/// <c>already-used</c>, núcleo anti-doble-entrada. El adapter real (DB / scanner)
/// implementa la misma seam. ADR 0075.
/// </remarks>
public sealed class StubEventManagementService : IEventManagementService
{
    private readonly EventTicketLedger _ledger;
    private readonly IEventCatalogProvider _catalog;

    public StubEventManagementService(EventTicketLedger ledger, IEventCatalogProvider catalog)
    {
        _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<EventManageView> GetManageAsync(string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
        {
            throw new ArgumentException("El evento es obligatorio.", nameof(eventId));
        }

        var detail = await _catalog.GetEventAsync(eventId, cancellationToken)
            ?? throw new ArgumentException($"Evento '{eventId}' no encontrado.", nameof(eventId));

        var attendees = await _ledger.ConfirmedAttendeesAsync(detail.Summary.Id, cancellationToken);

        // Aforo total = suma de la capacidad de los tiers. Vendidos = tickets
        // confirmados (las unidades emitidas por el motor de ticketing).
        var capacity = detail.Tiers.Sum(t => t.Capacity);
        var sold = attendees.Count;

        return new EventManageView(
            EventId: detail.Summary.Id,
            Attendees: attendees,
            Capacity: capacity,
            Sold: sold);
    }

    /// <remarks>
    /// Se usa la variante DETALLADA (T7): además del estado devuelve de qué evento y
    /// asistente se trataba, que es lo que el aviso en vivo necesita para elegir el canal
    /// y nombrar a quien acaba de entrar.
    /// </remarks>
    public Task<EventCheckInResult> CheckInAsync(string ticketId, CancellationToken cancellationToken = default)
        => _ledger.CheckInAsync(ticketId, cancellationToken);

    public async Task<EventCreateResult> CreateEventAsync(EventDraft draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (string.IsNullOrWhiteSpace(draft.Name))
        {
            throw new ArgumentException("El nombre del evento es obligatorio.", nameof(draft));
        }
        if (string.IsNullOrWhiteSpace(draft.Venue))
        {
            throw new ArgumentException("El venue del evento es obligatorio.", nameof(draft));
        }
        if (draft.Tiers is null || draft.Tiers.Count == 0)
        {
            throw new ArgumentException("El evento requiere al menos un tier.", nameof(draft));
        }

        var currency = string.IsNullOrWhiteSpace(draft.Currency) ? DefaultCurrency : draft.Currency!.Trim();

        // Validar + mapear cada tier draft → EventTier (aforo > 0 obligatorio;
        // el código se deriva del nombre y se desambigua si colisiona).
        var tiers = new List<EventTier>(draft.Tiers.Count);
        var usedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in draft.Tiers)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Name))
            {
                throw new ArgumentException("Cada tier requiere un nombre.", nameof(draft));
            }
            if (t.Capacity <= 0)
            {
                throw new ArgumentException($"El aforo del tier '{t.Name}' debe ser mayor a cero.", nameof(draft));
            }
            if (t.Price < 0)
            {
                throw new ArgumentException($"El precio del tier '{t.Name}' no puede ser negativo.", nameof(draft));
            }

            var code = DeriveTierCode(t.Name, usedCodes);
            usedCodes.Add(code);
            tiers.Add(new EventTier(
                Code: code,
                Name: t.Name.Trim(),
                Price: t.Price,
                Currency: currency,
                Capacity: t.Capacity,
                Remaining: t.Capacity,
                MaxPerOrder: Math.Min(t.Capacity, DefaultMaxPerOrder)));
        }

        // El id lo asigna el catálogo al publicar (se envía vacío y se lee del retorno):
        // pedírselo al static de la clase concreta ataba esta cara de organizador al stub,
        // de modo que registrar otro IEventCatalogProvider dejaba el swap a medias.
        // Modo derivado del seat-map.
        var name = draft.Name.Trim();
        var slug = Slugify(name);
        var mode = draft.SeatMap is not null ? "reserved" : "general";
        var priceFrom = tiers.Min(t => t.Price);

        var detail = new EventDetail(
            Summary: new EventSummary(
                Id: string.Empty,   // lo asigna PublishEventAsync
                Slug: slug,
                Title: name,
                Category: string.IsNullOrWhiteSpace(draft.Category) ? "Evento" : draft.Category!.Trim(),
                City: string.IsNullOrWhiteSpace(draft.City) ? string.Empty : draft.City!.Trim(),
                Venue: draft.Venue.Trim(),
                StartUtc: draft.Date,
                ImageUrl: string.IsNullOrWhiteSpace(draft.ImageUrl) ? string.Empty : draft.ImageUrl!.Trim(),
                PriceFrom: priceFrom,
                Currency: currency,
                Mode: mode,
                Geo: null),
            Description: string.IsNullOrWhiteSpace(draft.Description) ? string.Empty : draft.Description!.Trim(),
            Organizer: string.IsNullOrWhiteSpace(draft.Organizer) ? "Organizador" : draft.Organizer!.Trim(),
            Tiers: tiers,
            SeatMap: draft.SeatMap);

        var published = await _catalog.PublishEventAsync(detail, cancellationToken);
        return new EventCreateResult(published.Summary.Id);
    }

    private const string DefaultCurrency = "COP";
    private const int DefaultMaxPerOrder = 10;

    // Código de tier derivado del nombre (primeras letras alfanuméricas en mayúscula);
    // desambigua con un sufijo numérico si colisiona con otro tier del mismo evento.
    private static string DeriveTierCode(string name, HashSet<string> used)
    {
        var letters = new string(name.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var baseCode = letters.Length == 0
            ? "TIER"
            : letters[..Math.Min(4, letters.Length)];
        var code = baseCode;
        var n = 2;
        while (used.Contains(code))
        {
            code = $"{baseCode}{n++}";
        }
        return code;
    }

    // Slug amable para URLs a partir del nombre del evento (ascii-ish, sin acentos
    // sofisticados — suficiente para la demo; el adapter real usa el del CMS).
    private static string Slugify(string name)
    {
        var chars = name.Trim().ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray();
        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }
        return slug.Trim('-');
    }
}
