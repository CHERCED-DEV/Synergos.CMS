using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPatientRegistry"/> — padrón de pacientes STUB del dashboard
/// EHR-lite (OLA 5), calcando <c>StubCourseCatalogProvider</c>: sirve un padrón
/// sembrado (<see cref="EhrDemoSeed"/>) en memoria para que la demo corra
/// end-to-end sin DB. Lógica pura (ADR 0002) — el adapter real (HIS/DB) reemplaza
/// el seam sin tocar el controller.
/// </summary>
public sealed class StubPatientRegistry : IPatientRegistry
{
    private readonly ConcurrentDictionary<string, EhrPatient> _patients;

    public StubPatientRegistry()
    {
        _patients = new ConcurrentDictionary<string, EhrPatient>(StringComparer.Ordinal);
        foreach (var p in EhrDemoSeed.Patients())
        {
            _patients[p.Id] = p;
        }
    }

    public Task<IReadOnlyList<EhrPatient>> SearchAsync(string? query, CancellationToken cancellationToken = default)
    {
        var all = _patients.Values.OrderBy(p => p.FullName, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<EhrPatient>>(all.ToList());
        }

        var q = query.Trim();
        var matches = all
            .Where(p =>
                p.FullName.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.DocumentId.Contains(q, StringComparison.OrdinalIgnoreCase)
                || p.Email.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<EhrPatient>>(matches);
    }

    public Task<EhrPatient?> GetAsync(string patientId, CancellationToken cancellationToken = default)
        => Task.FromResult(_patients.TryGetValue(patientId ?? string.Empty, out var p) ? p : null);
}
