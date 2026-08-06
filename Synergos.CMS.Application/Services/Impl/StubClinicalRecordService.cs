using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalRecordService"/> — historia clínica + encuentros
/// (SOAP) STUB del dashboard EHR-lite (OLA 5). Mantiene la historia y los
/// encuentros sembrados (<see cref="EhrDemoSeed"/>) en memoria del proceso;
/// lógica pura (ADR 0002). El adapter real (PHI store / FHIR) reemplaza el seam
/// sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting (spec §4):</strong> CADA lectura y escritura emite un
/// evento append-only a <see cref="IAuditTrailWriter"/> (ADR 0037, reuso directo) —
/// el rastro de acceso a datos clínicos es requisito del EHR. Acción canónica
/// <c>{resource}.{verb}</c> (<c>clinical-record.read</c>, <c>encounter.create</c>).
/// La escritura de auditoría es best-effort en este seam de demo (no fail-closed
/// como el guard de producción de ADR 0098); la captura de errores no debe tumbar
/// la demo.
/// </remarks>
public sealed class StubClinicalRecordService : IClinicalRecordService
{
    private const string PhiActor = "system@synergos.demo";
    private const string PhiActorName = "EHR Demo";

    private readonly IAuditTrailWriter _audit;
    private readonly Func<DateTime> _nowUtc;

    private readonly ConcurrentDictionary<string, ClinicalHistory> _histories;
    // patientId → encuentros (más reciente primero se ordena al leer).
    private readonly ConcurrentDictionary<string, List<ClinicalEncounter>> _encounters;

    public StubClinicalRecordService(IAuditTrailWriter audit)
        : this(audit, null)
    {
    }

    /// <summary>
    /// Ctor con time source inyectable (<paramref name="nowUtc"/>) para determinismo
    /// en tests (ADR 0002). Null = reloj real UTC.
    /// </summary>
    public StubClinicalRecordService(IAuditTrailWriter audit, Func<DateTime>? nowUtc)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        var now = _nowUtc();
        _histories = new ConcurrentDictionary<string, ClinicalHistory>(EhrDemoSeed.Histories(), StringComparer.Ordinal);

        _encounters = new ConcurrentDictionary<string, List<ClinicalEncounter>>(StringComparer.Ordinal);
        foreach (var grp in EhrDemoSeed.Encounters(now).GroupBy(e => e.PatientId, StringComparer.Ordinal))
        {
            _encounters[grp.Key] = grp.ToList();
        }
    }

    public async Task<ClinicalHistory?> GetHistoryAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("clinical-record.read", key, "history", cancellationToken);

        if (!_histories.TryGetValue(key, out var history))
        {
            return null;
        }

        // Deriva los campos volátiles (última visita + total) desde los encuentros
        // vivos para que el chart sea coherente tras nuevos AddEncounter.
        var encounters = ReadEncounters(key);
        return history with
        {
            LastVisitUtc = encounters.Count > 0 ? encounters[0].OccurredAtUtc : history.LastVisitUtc,
            TotalEncounters = encounters.Count,
        };
    }

    public async Task<IReadOnlyList<ClinicalEncounter>> GetEncountersAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("clinical-record.read", key, "encounters", cancellationToken);
        return ReadEncounters(key);
    }

    public async Task<ClinicalEncounter> AddEncounterAsync(AddEncounterRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            throw new ArgumentException("El paciente es obligatorio.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.DoctorId))
        {
            throw new ArgumentException("El médico es obligatorio.", nameof(request));
        }
        ArgumentNullException.ThrowIfNull(request.Soap);

        var doctor = EhrDemoSeed.Doctors().FirstOrDefault(d => string.Equals(d.Id, request.DoctorId, StringComparison.Ordinal));

        var encounter = new ClinicalEncounter(
            Id: $"enc_{Guid.NewGuid():N}",
            PatientId: request.PatientId,
            DoctorId: request.DoctorId,
            DoctorName: doctor?.FullName ?? request.DoctorId,
            OccurredAtUtc: _nowUtc(),
            ReasonForVisit: request.ReasonForVisit ?? string.Empty,
            Soap: request.Soap,
            DiagnosisCode: request.DiagnosisCode,
            SignedByClinician: true);

        var list = _encounters.GetOrAdd(request.PatientId, _ => new List<ClinicalEncounter>());
        lock (list)
        {
            list.Add(encounter);
        }

        await AuditPhiAsync("encounter.create", request.PatientId, encounter.Id, cancellationToken);
        return encounter;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    private IReadOnlyList<ClinicalEncounter> ReadEncounters(string patientId)
    {
        if (!_encounters.TryGetValue(patientId, out var list))
        {
            return Array.Empty<ClinicalEncounter>();
        }
        lock (list)
        {
            return list.OrderByDescending(e => e.OccurredAtUtc).ToList();
        }
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
            // Best-effort en la demo: un fallo de auditoría no debe tumbar la lectura
            // del chart. La capa de producción (ADR 0098) es fail-closed; este stub no.
        }
    }
}
