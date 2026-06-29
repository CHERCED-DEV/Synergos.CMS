using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ILeadCaptureService"/> — captura de leads STUB del portal
/// inmobiliario (doc propiedades-app-spec §4). Genera un lead a partir del listado
/// de interés + contacto + mensaje y lo persiste en memoria del proceso. El lead
/// es el caso degenerado del "confirmar" transaccional (captura de intención sin
/// slot ni pago).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA (no crea seams nuevos):
/// <see cref="IAuditTrailWriter"/> (ADR 0037) deja rastro append-only forense del
/// lead, e <see cref="IAnalyticsTracker"/> (ADR 0067) emite el evento de negocio
/// <c>realty.lead-captured</c>. Ambos son opcionales (null = no-op) para que los
/// tests del seam corran aislados sin mocks obligatorios. El adapter real rutea el
/// lead a un CRM/email del agente sin tocar el motor. ADR 0075.
/// </remarks>
public sealed class StubLeadCaptureService : ILeadCaptureService
{
    private readonly IAuditTrailWriter? _audit;
    private readonly IAnalyticsTracker? _analytics;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, LeadRecord> _leads = new(StringComparer.Ordinal);

    public StubLeadCaptureService()
        : this(null, null, null)
    {
    }

    /// <summary>
    /// Ctor con los seams reusados opcionales (audit + analytics) y time source
    /// inyectable para determinismo en tests. Null en cualquiera = no-op.
    /// </summary>
    public StubLeadCaptureService(IAuditTrailWriter? audit, IAnalyticsTracker? analytics, Func<DateTimeOffset>? now)
    {
        _audit = audit;
        _analytics = analytics;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<LeadResult> CaptureAsync(string listingId, VisitContact contact, string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(listingId))
        {
            throw new ArgumentException("El listado es obligatorio.", nameof(listingId));
        }
        if (contact is null || string.IsNullOrWhiteSpace(contact.Name) || string.IsNullOrWhiteSpace(contact.Email))
        {
            throw new ArgumentException("El nombre y el email del interesado son obligatorios.", nameof(contact));
        }
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("El mensaje es obligatorio.", nameof(message));
        }

        var listing = listingId.Trim();
        var leadId = $"lead_{Guid.NewGuid():N}";
        var occurred = _now();

        _leads[leadId] = new LeadRecord(
            leadId, listing, contact.Name.Trim(), contact.Email.Trim(),
            contact.Phone?.Trim(), message.Trim(), occurred);

        // Audit append-only (forense) — opcional.
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: leadId,
                    OccurredAtUtc: occurred.UtcDateTime,
                    ActorEmail: contact.Email.Trim(),
                    ActorName: contact.Name.Trim(),
                    Action: "realty.lead-captured",
                    Resource: listing,
                    Outcome: "success",
                    Detail: $"Lead generado para el listado {listing}."),
                cancellationToken);
        }

        // Evento de negocio (KPI del marketplace) — opcional, fire-and-forget.
        _analytics?.Track("realty.lead-captured", new Dictionary<string, object?>
        {
            ["leadId"] = leadId,
            ["listingId"] = listing,
        });

        return new LeadResult(leadId);
    }

    private sealed record LeadRecord(
        string LeadId,
        string ListingId,
        string Name,
        string Email,
        string? Phone,
        string Message,
        DateTimeOffset CreatedAt);
}
