using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IClinicalOrderService"/> — order-entry clínico STUB del
/// dashboard EHR-lite (OLA 7). Lógica pura (ADR 0002); estado en memoria del proceso.
/// El adapter real (CPOE / eRx gateway) reemplaza el seam sin tocar el controller.
/// </summary>
/// <remarks>
/// <strong>Cierra el bucle order→result (DIP):</strong> al colocar una orden de
/// laboratorio, COMPONE <see cref="IClinicalResultsProvider"/> para liberar un
/// resultado coherente (demo de "order → result → release" del spec) — no duplica el
/// store de resultados. Resuelve el nombre del proveedor del
/// <see cref="IDoctorDirectory"/> sembrado. Cada orden se audita vía
/// <see cref="IAuditTrailWriter"/> (ADR 0037), best-effort en la demo.
/// </remarks>
public sealed class StubClinicalOrderService : IClinicalOrderService
{
    private const string PhiActor = "system@synergos.demo";
    private const string PhiActorName = "EHR Demo";

    private static readonly HashSet<string> AllowedTypes =
        new(new[] { "lab", "eRx", "imaging", "referral" }, StringComparer.OrdinalIgnoreCase);

    private readonly IDoctorDirectory _doctors;
    private readonly IClinicalResultsProvider _results;
    private readonly IAuditTrailWriter _audit;
    private readonly Func<DateTime> _nowUtc;

    private readonly ConcurrentDictionary<string, List<EhrClinicalOrder>> _byPatient = new(StringComparer.Ordinal);

    public StubClinicalOrderService(
        IDoctorDirectory doctors,
        IClinicalResultsProvider results,
        IAuditTrailWriter audit)
        : this(doctors, results, audit, null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests (ADR 0002).</summary>
    public StubClinicalOrderService(
        IDoctorDirectory doctors,
        IClinicalResultsProvider results,
        IAuditTrailWriter audit,
        Func<DateTime>? nowUtc)
    {
        _doctors = doctors ?? throw new ArgumentNullException(nameof(doctors));
        _results = results ?? throw new ArgumentNullException(nameof(results));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
    }

    public async Task<EhrClinicalOrder> PlaceAsync(PlaceOrderRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.PatientId))
        {
            throw new ArgumentException("El paciente es obligatorio.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ProviderId))
        {
            throw new ArgumentException("El proveedor es obligatorio.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Type) || !AllowedTypes.Contains(request.Type))
        {
            throw new ArgumentException("El tipo de orden debe ser lab | eRx | imaging | referral.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Detail))
        {
            throw new ArgumentException("El detalle de la orden es obligatorio.", nameof(request));
        }

        var doctor = await _doctors.GetAsync(request.ProviderId, cancellationToken);
        var isLab = string.Equals(request.Type, "lab", StringComparison.OrdinalIgnoreCase);

        var order = new EhrClinicalOrder(
            Id: $"ord_{Guid.NewGuid():N}",
            PatientId: request.PatientId,
            ProviderId: request.ProviderId,
            ProviderName: doctor?.FullName ?? request.ProviderId,
            Type: request.Type,
            Detail: request.Detail,
            PlacedAtUtc: _nowUtc(),
            // Lab → queda "resulted" porque libera el resultado abajo; el resto "placed".
            Status: isLab ? "resulted" : "placed");

        var list = _byPatient.GetOrAdd(request.PatientId, _ => new List<EhrClinicalOrder>());
        lock (list)
        {
            list.Add(order);
        }

        await AuditPhiAsync("order.place", request.PatientId, $"{order.Type}:{order.Id}", cancellationToken);

        // Bucle order→result: una orden de lab libera un resultado coherente (DIP).
        if (isLab)
        {
            await ReleaseResultForLabAsync(order, cancellationToken);
        }

        return order;
    }

    public async Task<IReadOnlyList<EhrClinicalOrder>> GetForPatientAsync(string patientId, CancellationToken cancellationToken = default)
    {
        var key = patientId ?? string.Empty;
        await AuditPhiAsync("order.read", key, "list", cancellationToken);

        if (!_byPatient.TryGetValue(key, out var list))
        {
            return Array.Empty<EhrClinicalOrder>();
        }
        lock (list)
        {
            return list.OrderByDescending(o => o.PlacedAtUtc).ToList();
        }
    }

    private async Task ReleaseResultForLabAsync(EhrClinicalOrder order, CancellationToken cancellationToken)
    {
        try
        {
            await _results.ReleaseResultAsync(
                new ReleaseLabResultRequest(
                    PatientId: order.PatientId,
                    PanelName: "Laboratorio",
                    TestName: order.Detail,
                    Value: "Pendiente de valor",
                    Unit: string.Empty,
                    ReferenceLow: null,
                    ReferenceHigh: null,
                    OrderId: order.Id,
                    OrderedByDoctorId: order.ProviderId,
                    Notes: "Resultado liberado desde el order-entry (demo)."),
                cancellationToken);
        }
        catch
        {
            // Best-effort en la demo: un fallo al liberar el resultado no tumba la orden.
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
