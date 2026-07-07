using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IEhrInBasketService"/> — In Basket del proveedor STUB del
/// dashboard EHR-lite (OLA 7; SH-7 v3). Lógica pura (ADR 0002). El adapter real
/// reemplaza el seam sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>DERIVA, no duplica (DIP):</strong> proyecta a una cola tipada los ítems
/// accionables de los seams vivos, sin store paralelo (misma técnica que
/// <c>StubNotificationFeed</c>):
/// <list type="bullet">
/// <item><c>result</c> — resultados anormales (flag ≠ normal) que ordenó el proveedor,
///   de <see cref="IClinicalResultsProvider"/> filtrando por
///   <see cref="EhrLabResult.OrderedByDoctorId"/>.</item>
/// <item><c>refill</c> — solicitudes de resurtido pendientes dirigidas al proveedor,
///   de la cara de lectura del <see cref="StubClinicalMedicationService"/> concreto.</item>
/// <item><c>message</c> — hilos del contexto <c>clinical</c> en los que participa el
///   proveedor, de <see cref="IMessagingService"/>.</item>
/// </list>
/// El padrón (<see cref="IPatientRegistry"/>) resuelve el nombre del paciente. Los
/// resultados se recorren solo para los pacientes del padrón sembrado (demo acotada).
/// </remarks>
public sealed class StubEhrInBasketService : IEhrInBasketService
{
    private readonly IClinicalResultsProvider _results;
    private readonly StubClinicalMedicationService _medications;
    private readonly IMessagingService _messaging;
    private readonly IPatientRegistry _patients;

    public StubEhrInBasketService(
        IClinicalResultsProvider results,
        StubClinicalMedicationService medications,
        IMessagingService messaging,
        IPatientRegistry patients)
    {
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _medications = medications ?? throw new ArgumentNullException(nameof(medications));
        _messaging = messaging ?? throw new ArgumentNullException(nameof(messaging));
        _patients = patients ?? throw new ArgumentNullException(nameof(patients));
    }

    public async Task<IReadOnlyList<EhrInBasketItem>> GetForProviderAsync(string providerId, string? type = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return Array.Empty<EhrInBasketItem>();
        }

        var patients = await _patients.SearchAsync(null, cancellationToken);
        var nameById = patients.ToDictionary(p => p.Id, p => p.FullName, StringComparer.Ordinal);

        var items = new List<EhrInBasketItem>();
        var wantResults = MatchesType(type, "result");
        var wantRefills = MatchesType(type, "refill");
        var wantMessages = MatchesType(type, "message");

        // ── result: resultados anormales que ordenó el proveedor ──────────
        if (wantResults)
        {
            foreach (var patient in patients)
            {
                var labs = await _results.GetForPatientAsync(patient.Id, cancellationToken);
                foreach (var lab in labs)
                {
                    if (!string.Equals(lab.OrderedByDoctorId, providerId, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    if (string.Equals(lab.Flag, "normal", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    items.Add(new EhrInBasketItem(
                        Id: $"ib-result-{lab.Id}",
                        Type: "result",
                        ProviderId: providerId,
                        PatientId: patient.Id,
                        PatientName: patient.FullName,
                        Title: $"Resultado anormal — {lab.TestName}",
                        Preview: $"{lab.Value} {lab.Unit}".Trim() + $" ({lab.Flag})",
                        Priority: string.Equals(lab.Flag, "critical", StringComparison.OrdinalIgnoreCase) ? "high" : "normal",
                        OccurredAtUtc: lab.ResultedAtUtc,
                        RefId: lab.Id));
                }
            }
        }

        // ── refill: solicitudes pendientes dirigidas al proveedor ─────────
        if (wantRefills)
        {
            foreach (var refill in _medications.GetPendingRefillsForProvider(providerId))
            {
                var patientName = nameById.TryGetValue(refill.PatientId, out var name) ? name : refill.PatientId;
                items.Add(new EhrInBasketItem(
                    Id: $"ib-refill-{refill.Id}",
                    Type: "refill",
                    ProviderId: providerId,
                    PatientId: refill.PatientId,
                    PatientName: patientName,
                    Title: $"Solicitud de resurtido — {refill.MedicationName}",
                    Preview: string.IsNullOrWhiteSpace(refill.Note) ? "Sin nota" : refill.Note!,
                    Priority: "normal",
                    OccurredAtUtc: refill.RequestedAtUtc,
                    RefId: refill.Id));
            }
        }

        // ── message: hilos clínicos en los que participa el proveedor ─────
        if (wantMessages)
        {
            var inbox = await _messaging.GetInboxAsync(providerId, cancellationToken);
            foreach (var thread in inbox)
            {
                if (!IsClinicalContext(thread.ContextRef))
                {
                    continue;
                }
                // Un hilo de refill ya se representa como ítem 'refill'; no duplicar.
                if (thread.ContextRef.StartsWith($"{StubClinicalMedicationService.ClinicalContext}:refill:", StringComparison.Ordinal))
                {
                    continue;
                }
                var counterpart = thread.Participants.FirstOrDefault(p => !string.Equals(p, providerId, StringComparison.Ordinal)) ?? string.Empty;
                var patientName = nameById.TryGetValue(counterpart, out var name) ? name : counterpart;
                items.Add(new EhrInBasketItem(
                    Id: $"ib-message-{thread.ThreadId}",
                    Type: "message",
                    ProviderId: providerId,
                    PatientId: nameById.ContainsKey(counterpart) ? counterpart : string.Empty,
                    PatientName: patientName,
                    Title: "Mensaje del paciente",
                    Preview: thread.LastMessagePreview,
                    Priority: "normal",
                    OccurredAtUtc: thread.LastMessageAt.UtcDateTime,
                    RefId: thread.ThreadId));
            }
        }

        return items.OrderByDescending(i => i.OccurredAtUtc).ToList();
    }

    private static bool MatchesType(string? filter, string type)
        => string.IsNullOrWhiteSpace(filter) || string.Equals(filter, type, StringComparison.OrdinalIgnoreCase);

    private static bool IsClinicalContext(string contextRef)
        => !string.IsNullOrEmpty(contextRef)
            && (string.Equals(contextRef, StubClinicalMedicationService.ClinicalContext, StringComparison.Ordinal)
                || contextRef.StartsWith($"{StubClinicalMedicationService.ClinicalContext}:", StringComparison.Ordinal));
}
