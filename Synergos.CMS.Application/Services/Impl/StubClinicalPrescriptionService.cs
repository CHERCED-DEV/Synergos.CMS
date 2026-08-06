using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalPrescriptionService"/> — recetas STUB del dashboard
/// EHR-lite (OLA 5). Mantiene las recetas sembradas (<see cref="EhrDemoSeed"/>) en
/// memoria del proceso; lógica pura (ADR 0002). RECORD-KEEPER: registra, NO valida
/// interacciones ni dosis. El adapter real reemplaza el seam sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting:</strong> cada lectura/escritura emite un evento a
/// <see cref="IAuditTrailWriter"/> (ADR 0037). Best-effort en la demo.
/// </remarks>
public sealed class StubClinicalPrescriptionService : IClinicalPrescriptionService
{
    private const string PhiActor = "system@synergos.demo";
    private const string PhiActorName = "EHR Demo";

    private readonly IAuditTrailWriter _audit;
    private readonly Func<DateTime> _nowUtc;
    private readonly ConcurrentDictionary<string, List<EhrPrescription>> _byPatient;

    public StubClinicalPrescriptionService(IAuditTrailWriter audit)
        : this(audit, null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests (ADR 0002).</summary>
    public StubClinicalPrescriptionService(IAuditTrailWriter audit, Func<DateTime>? nowUtc)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        _byPatient = new ConcurrentDictionary<string, List<EhrPrescription>>(StringComparer.Ordinal);
        foreach (var grp in EhrDemoSeed.Prescriptions(_nowUtc()).GroupBy(p => p.PatientId, StringComparer.Ordinal))
        {
            _byPatient[grp.Key] = grp.ToList();
        }
    }

    public async Task<EhrPrescription> AddAsync(AddPrescriptionRequest request, CancellationToken cancellationToken = default)
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
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("La receta requiere al menos un medicamento.", nameof(request));
        }

        var doctor = EhrDemoSeed.Doctors().FirstOrDefault(d => string.Equals(d.Id, request.DoctorId, StringComparison.Ordinal));

        var prescription = new EhrPrescription(
            Id: $"rx_{Guid.NewGuid():N}",
            PatientId: request.PatientId,
            DoctorId: request.DoctorId,
            DoctorName: doctor?.FullName ?? request.DoctorId,
            IssuedAtUtc: _nowUtc(),
            Items: request.Items.ToList(),
            Status: "active",
            EncounterId: request.EncounterId);

        var list = _byPatient.GetOrAdd(request.PatientId, _ => new List<EhrPrescription>());
        lock (list)
        {
            list.Add(prescription);
        }

        await AuditPhiAsync("prescription.create", request.PatientId, prescription.Id, cancellationToken);
        return prescription;
    }

    public async Task<IReadOnlyList<EhrPrescription>> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("prescription.read", key, "list", cancellationToken);

        if (!_byPatient.TryGetValue(key, out var list))
        {
            return Array.Empty<EhrPrescription>();
        }
        lock (list)
        {
            return list.OrderByDescending(p => p.IssuedAtUtc).ToList();
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
            // Best-effort en la demo (ver StubClinicalRecordService).
        }
    }
}
