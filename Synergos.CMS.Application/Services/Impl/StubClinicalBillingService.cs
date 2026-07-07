using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalBillingService"/> — facturación STUB del dashboard
/// EHR-lite (OLA 7). Sirve el estado de cuenta sembrado (<see cref="EhrDemoSeed"/>) en
/// memoria del proceso; lógica pura (ADR 0002). Solo lectura — el adapter real
/// (billing / RCM) reemplaza el seam sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting:</strong> cada lectura emite un evento a
/// <see cref="IAuditTrailWriter"/> (ADR 0037). Best-effort en la demo.
/// </remarks>
public sealed class StubClinicalBillingService : IClinicalBillingService
{
    private const string PhiActor = "system@synergos.demo";
    private const string PhiActorName = "EHR Demo";

    private readonly IAuditTrailWriter _audit;
    private readonly Func<DateTime> _nowUtc;
    private readonly ConcurrentDictionary<string, EhrBillingStatement> _byPatient;

    public StubClinicalBillingService(IAuditTrailWriter audit)
        : this(audit, null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests (ADR 0002).</summary>
    public StubClinicalBillingService(IAuditTrailWriter audit, Func<DateTime>? nowUtc)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        _byPatient = new ConcurrentDictionary<string, EhrBillingStatement>(EhrDemoSeed.Billing(_nowUtc()), StringComparer.Ordinal);
    }

    public async Task<EhrBillingStatement?> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("billing.read", key, "statement", cancellationToken);
        return _byPatient.TryGetValue(key, out var statement) ? statement : null;
    }

    private async Task AuditPhiAsync(string action, string patientId, string detail, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: Guid.NewGuid().ToString("N"),
                    OccurredAtUtc: _nowUtc(),
                    ActorEmail: PhiActor,
                    ActorName: PhiActorName,
                    Action: action,
                    Resource: $"patient:{patientId}",
                    Outcome: "success",
                    Detail: detail),
                cancellationToken);
        }
        catch
        {
            // Best-effort en la demo (ver StubClinicalRecordService).
        }
    }
}
