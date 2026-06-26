using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPrescriptionService"/> (ADR 0098). Persiste recetas sobre
/// <see cref="IPhiStore"/> (cifrado/atómico). RECORD-KEEPER: emite el registro sin
/// validación clínica alguna. La autorización la hace el <see cref="IPhiAccessGuard"/>.
/// </summary>
public sealed class FileSystemPrescriptionService : IPrescriptionService
{
    private const string ResourceType = "prescriptions";

    private readonly IPhiStore _store;

    public FileSystemPrescriptionService(IPhiStore store) => _store = store;

    public async Task<PrescriptionRecord> IssueAsync(IssuePrescriptionRequest request, CancellationToken cancellationToken)
    {
        var record = new PrescriptionRecord(
            Guid.NewGuid(), request.PatientKey, request.IssuedByDoctorKey,
            request.MedicationName, request.Dosage, request.Frequency, request.Instructions,
            request.ValidFromUtc, request.ValidToUtc, DateTime.UtcNow, "active");
        await _store.WriteAsync(ResourceType, record.PrescriptionId, JsonSerializer.Serialize(record), cancellationToken)
            .ConfigureAwait(false);
        return record;
    }

    public async Task<PrescriptionRecord?> GetAsync(Guid prescriptionId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, prescriptionId, cancellationToken).ConfigureAwait(false);
        return json is null ? null : Deserialize(json);
    }

    public async Task<IReadOnlyList<PrescriptionRecord>> ListForPatientAsync(Guid patientKey, CancellationToken cancellationToken)
    {
        var raw = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
        var result = new List<PrescriptionRecord>();
        foreach (var json in raw)
        {
            var r = Deserialize(json);
            if (r is not null && r.PatientKey == patientKey) { result.Add(r); }
        }
        return result.OrderByDescending(r => r.IssuedAtUtc).ToList();
    }

    public async Task<bool> RevokeAsync(Guid prescriptionId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, prescriptionId, cancellationToken).ConfigureAwait(false);
        if (json is null) { return false; }
        var record = Deserialize(json);
        if (record is null) { return false; }

        var revoked = record with { Status = "revoked" };
        await _store.WriteAsync(ResourceType, prescriptionId, JsonSerializer.Serialize(revoked), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    private static PrescriptionRecord? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<PrescriptionRecord>(json); }
        catch (JsonException) { return null; }
    }
}
