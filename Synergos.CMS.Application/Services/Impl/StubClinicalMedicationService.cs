using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalMedicationService"/> — medicación activa + refills
/// STUB del dashboard EHR-lite (OLA 7). Lógica pura (ADR 0002); estado en memoria del
/// proceso. El adapter real (pharmacy / eRx) reemplaza el seam sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>DERIVA la medicación de las recetas vivas (DIP):</strong> compone
/// <see cref="IClinicalPrescriptionService"/> — cada línea de una receta <c>active</c>
/// es una medicación activa; no hay store paralelo. El refill:
/// <list type="number">
/// <item>Valida que la medicación exista en el padrón activo del paciente.</item>
/// <item>Enruta la solicitud al In Basket del médico prescriptor vía
///   <see cref="IMessagingService"/> (contexto <c>clinical</c>) — el mismo seam
///   genérico de hilos que Tienda/Blogs, reusado.</item>
/// <item>Audita la escritura vía <see cref="IAuditTrailWriter"/> (ADR 0037).</item>
/// </list>
/// Best-effort en la demo (un fallo de audit/mensajería no tumba la solicitud).
/// </remarks>
public sealed class StubClinicalMedicationService : IClinicalMedicationService
{
    private const string PhiActor = "system@synergos.demo";
    private const string PhiActorName = "EHR Demo";
    /// <summary>Contexto del hilo de mensajería para el In Basket clínico.</summary>
    public const string ClinicalContext = "clinical";

    private readonly IClinicalPrescriptionService _prescriptions;
    private readonly IMessagingService _messaging;
    private readonly IAuditTrailWriter _audit;
    private readonly Func<DateTime> _nowUtc;

    private readonly ConcurrentDictionary<string, EhrRefillRequest> _refills = new(StringComparer.Ordinal);

    public StubClinicalMedicationService(
        IClinicalPrescriptionService prescriptions,
        IMessagingService messaging,
        IAuditTrailWriter audit)
        : this(prescriptions, messaging, audit, null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests (ADR 0002).</summary>
    public StubClinicalMedicationService(
        IClinicalPrescriptionService prescriptions,
        IMessagingService messaging,
        IAuditTrailWriter audit,
        Func<DateTime>? nowUtc)
    {
        _prescriptions = prescriptions ?? throw new ArgumentNullException(nameof(prescriptions));
        _messaging = messaging ?? throw new ArgumentNullException(nameof(messaging));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<EhrMedication>> GetActiveForPatientAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("medication.read", key, "list", cancellationToken);
        return await BuildActiveMedicationsAsync(key, cancellationToken);
    }

    /// <summary>
    /// Cara de lectura de las solicitudes de refill vivas, por proveedor prescriptor
    /// (más reciente primero). La consume el In Basket clínico por composición (DIP,
    /// misma técnica que <c>StubContentStream</c>→<c>StubReactionService</c>) — no está
    /// en <see cref="IClinicalMedicationService"/> para mantener el ISP del portal
    /// paciente. Devuelve solo las <c>pending</c> por defecto.
    /// </summary>
    public IReadOnlyList<EhrRefillRequest> GetPendingRefillsForProvider(string providerId)
        => _refills.Values
            .Where(r => string.Equals(r.ProviderId, providerId, StringComparison.Ordinal))
            .Where(r => string.Equals(r.Status, "pending", StringComparison.Ordinal))
            .OrderByDescending(r => r.RequestedAtUtc)
            .ToList();

    public async Task<EhrRefillRequest> RequestRefillAsync(RefillRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            throw new ArgumentException("El paciente es obligatorio.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.MedicationId))
        {
            throw new ArgumentException("La medicación es obligatoria.", nameof(request));
        }

        var meds = await BuildActiveMedicationsAsync(request.PatientId, cancellationToken);
        var med = meds.FirstOrDefault(m => string.Equals(m.MedicationId, request.MedicationId, StringComparison.Ordinal))
            ?? throw new ArgumentException($"La medicación '{request.MedicationId}' no está activa para el paciente.", nameof(request));

        var refill = new EhrRefillRequest(
            Id: $"refill_{Guid.NewGuid():N}",
            PatientId: request.PatientId,
            MedicationId: med.MedicationId,
            MedicationName: med.MedicationName,
            ProviderId: med.PrescribedByDoctorId,
            RequestedAtUtc: _nowUtc(),
            Status: "pending",
            Note: request.Note);

        _refills[refill.Id] = refill;

        // Enruta al In Basket del médico prescriptor (hilo genérico, contexto clinical).
        await RouteToInBasketAsync(refill, cancellationToken);
        await AuditPhiAsync("refill.request", request.PatientId, refill.Id, cancellationToken);
        return refill;
    }

    // ── Helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Proyecta las recetas <c>active</c> del paciente a medicaciones activas: cada
    /// ítem de cada receta es una medicación con id estable derivado de la receta +
    /// índice del ítem (para poder solicitar su refill).
    /// </summary>
    private async Task<IReadOnlyList<EhrMedication>> BuildActiveMedicationsAsync(string patientId, CancellationToken cancellationToken)
    {
        var prescriptions = await _prescriptions.GetForPatientAsync(patientId, cancellationToken);
        var meds = new List<EhrMedication>();
        foreach (var rx in prescriptions)
        {
            if (!string.Equals(rx.Status, "active", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            for (var i = 0; i < rx.Items.Count; i++)
            {
                var item = rx.Items[i];
                meds.Add(new EhrMedication(
                    MedicationId: $"{rx.Id}:{i}",
                    PatientId: rx.PatientId,
                    MedicationName: item.MedicationName,
                    Dosage: item.Dosage,
                    Frequency: item.Frequency,
                    Instructions: item.Instructions,
                    PrescribedByDoctorId: rx.DoctorId,
                    PrescribedByDoctorName: rx.DoctorName,
                    PrescribedAtUtc: rx.IssuedAtUtc,
                    Status: rx.Status,
                    RefillsRemaining: null));
            }
        }
        return meds.OrderByDescending(m => m.PrescribedAtUtc).ToList();
    }

    private async Task RouteToInBasketAsync(EhrRefillRequest refill, CancellationToken cancellationToken)
    {
        try
        {
            var body = $"Solicitud de resurtido — {refill.MedicationName}"
                + (string.IsNullOrWhiteSpace(refill.Note) ? string.Empty : $": {refill.Note}");
            await _messaging.StartThreadAsync(
                contextRef: $"{ClinicalContext}:refill:{refill.Id}",
                from: refill.PatientId,
                to: refill.ProviderId,
                body: body,
                cancellationToken);
        }
        catch
        {
            // Best-effort en la demo: un fallo de mensajería no tumba la solicitud.
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
