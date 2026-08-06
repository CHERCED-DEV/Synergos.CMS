using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IAppointmentScheduler"/> (ADR 0098). Persiste citas sobre
/// <see cref="IPhiStore"/> (cifrado/atómico) y serializa la reserva por-doctor
/// con un <see cref="SemaphoreSlim"/> async, de modo que el read-check-write
/// anti-overbooking es atómico. La decisión de conflicto es pura
/// (<see cref="AppointmentSchedulingService"/>).
/// </summary>
/// <remarks>
/// Lock in-process (single-instance, ADR 0098 D1). Multi-instancia requeriría un
/// lock distribuido (Redis), pero la lógica pura y el seam quedan iguales.
/// </remarks>
public sealed class LockingAppointmentScheduler : IAppointmentScheduler
{
    private const string ResourceType = "appointments";

    private readonly IPhiStore _store;
    private readonly AppointmentSchedulingService _scheduling;
    private readonly IOptions<HealthcareSettings> _settings;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _doctorLocks = new();

    public LockingAppointmentScheduler(
        IPhiStore store,
        AppointmentSchedulingService scheduling,
        IOptions<HealthcareSettings> settings)
    {
        _store = store;
        _scheduling = scheduling;
        _settings = settings;
    }

    public async Task<AppointmentBookResult> BookAsync(AppointmentRequest request, CancellationToken cancellationToken)
    {
        if (request.EndUtc <= request.StartUtc)
        {
            return new AppointmentBookResult(false, null, "invalid-range");
        }

        var gate = _doctorLocks.GetOrAdd(request.DoctorKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // read-check-write atómico por-doctor.
            var existing = await ReadForDoctorAsync(request.DoctorKey, cancellationToken).ConfigureAwait(false);
            if (_scheduling.HasConflict(existing, request.StartUtc, request.EndUtc, _settings.Value.MaxOverbookingMinutes))
            {
                return new AppointmentBookResult(false, null, "overbooking-conflict");
            }

            var slot = new AppointmentSlot(
                Guid.NewGuid(), request.PatientKey, request.DoctorKey,
                request.StartUtc, request.EndUtc, "booked", null, DateTime.UtcNow, request.CreatedByKey);
            await _store.WriteAsync(ResourceType, slot.AppointmentId, JsonSerializer.Serialize(slot), cancellationToken)
                .ConfigureAwait(false);
            return new AppointmentBookResult(true, slot, null);
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task<bool> CancelAsync(Guid appointmentId, string reason, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, appointmentId, cancellationToken).ConfigureAwait(false);
        if (json is null) { return false; }
        var slot = Deserialize(json);
        if (slot is null) { return false; }

        var cancelled = slot with { Status = "cancelled", CancellationReason = reason };
        await _store.WriteAsync(ResourceType, appointmentId, JsonSerializer.Serialize(cancelled), cancellationToken)
            .ConfigureAwait(false);
        return true;
    }

    public async Task<IReadOnlyList<AppointmentSlot>> ListAsync(AppointmentQuery query, CancellationToken cancellationToken)
    {
        var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var limit = query.Limit > 0 ? query.Limit : 500;
        return all
            .Where(a => query.IncludeCancelled || !string.Equals(a.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
            .Where(a => query.DoctorKey is not Guid dk || a.DoctorKey == dk)
            .Where(a => query.PatientKey is not Guid pk || a.PatientKey == pk)
            .Where(a => query.FromUtc is not DateTime f || a.StartUtc >= f)
            .Where(a => query.ToUtc is not DateTime t || a.StartUtc <= t)
            .OrderBy(a => a.StartUtc)
            .Take(limit)
            .ToList();
    }

    private async Task<List<AppointmentSlot>> ReadForDoctorAsync(Guid doctorKey, CancellationToken ct)
    {
        var all = await ReadAllAsync(ct).ConfigureAwait(false);
        return all.Where(a => a.DoctorKey == doctorKey).ToList();
    }

    private async Task<List<AppointmentSlot>> ReadAllAsync(CancellationToken ct)
    {
        var raw = await _store.ListAsync(ResourceType, ct).ConfigureAwait(false);
        var result = new List<AppointmentSlot>(raw.Count);
        foreach (var json in raw)
        {
            var slot = Deserialize(json);
            if (slot is not null) { result.Add(slot); }
        }
        return result;
    }

    private static AppointmentSlot? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<AppointmentSlot>(json); }
        catch (JsonException) { return null; }
    }
}
