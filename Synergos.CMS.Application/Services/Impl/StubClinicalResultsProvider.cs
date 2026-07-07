using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalResultsProvider"/> — resultados de laboratorio STUB
/// del dashboard EHR-lite (OLA 7). Mantiene el panel sembrado (<see cref="EhrDemoSeed"/>)
/// en memoria del proceso; lógica pura (ADR 0002). El adapter real (LIS / HL7-ORU)
/// reemplaza el seam sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>PHI cross-cutting:</strong> cada lectura/escritura emite un evento a
/// <see cref="IAuditTrailWriter"/> (ADR 0037). Best-effort en la demo (ver
/// <see cref="StubClinicalRecordService"/>). El flag se recalcula al liberar un
/// resultado a partir del rango de referencia para mantener la coherencia.
/// </remarks>
public sealed class StubClinicalResultsProvider : IClinicalResultsProvider
{
    private const string PhiActor = "system@synergos.demo";
    private const string PhiActorName = "EHR Demo";

    private readonly IAuditTrailWriter _audit;
    private readonly Func<DateTime> _nowUtc;
    private readonly ConcurrentDictionary<string, List<EhrLabResult>> _byPatient;

    public StubClinicalResultsProvider(IAuditTrailWriter audit)
        : this(audit, null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests (ADR 0002).</summary>
    public StubClinicalResultsProvider(IAuditTrailWriter audit, Func<DateTime>? nowUtc)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);

        _byPatient = new ConcurrentDictionary<string, List<EhrLabResult>>(StringComparer.Ordinal);
        foreach (var grp in EhrDemoSeed.LabResults(_nowUtc()).GroupBy(r => r.PatientId, StringComparer.Ordinal))
        {
            _byPatient[grp.Key] = grp.ToList();
        }
    }

    public async Task<IReadOnlyList<EhrLabResult>> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("lab-result.read", key, "list", cancellationToken);

        if (!_byPatient.TryGetValue(key, out var list))
        {
            return Array.Empty<EhrLabResult>();
        }
        lock (list)
        {
            return list.OrderByDescending(r => r.ResultedAtUtc).ToList();
        }
    }

    public async Task<EhrLabResult> ReleaseResultAsync(ReleaseLabResultRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            throw new ArgumentException("El paciente es obligatorio.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.TestName))
        {
            throw new ArgumentException("La prueba es obligatoria.", nameof(request));
        }

        var result = new EhrLabResult(
            Id: $"lab_{Guid.NewGuid():N}",
            PatientId: request.PatientId,
            PanelName: string.IsNullOrWhiteSpace(request.PanelName) ? "Laboratorio" : request.PanelName,
            TestName: request.TestName,
            Value: request.Value ?? string.Empty,
            Unit: request.Unit ?? string.Empty,
            ReferenceLow: request.ReferenceLow,
            ReferenceHigh: request.ReferenceHigh,
            Flag: DeriveFlag(request.Value, request.ReferenceLow, request.ReferenceHigh),
            ResultedAtUtc: _nowUtc(),
            OrderId: request.OrderId,
            OrderedByDoctorId: request.OrderedByDoctorId,
            Notes: request.Notes);

        var list = _byPatient.GetOrAdd(request.PatientId, _ => new List<EhrLabResult>());
        lock (list)
        {
            list.Add(result);
        }

        await AuditPhiAsync("lab-result.release", request.PatientId, result.Id, cancellationToken);
        return result;
    }

    /// <summary>
    /// Deriva el flag a partir del valor numérico contra el rango de referencia:
    /// low si &lt; ReferenceLow, high si &gt; ReferenceHigh, normal en rango.
    /// Si el valor no es numérico o falta el rango → normal (prueba cualitativa).
    /// </summary>
    private static string DeriveFlag(string? value, double? low, double? high)
    {
        if (!double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            return "normal";
        }
        if (low.HasValue && v < low.Value)
        {
            return "low";
        }
        if (high.HasValue && v > high.Value)
        {
            return "high";
        }
        return "normal";
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
