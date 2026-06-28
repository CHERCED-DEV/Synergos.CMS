using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IDoctorDirectory"/> — directorio de médicos STUB del
/// dashboard EHR-lite (OLA 5). Sirve el staff sembrado (<see cref="EhrDemoSeed"/>)
/// en memoria; lógica pura (ADR 0002). El adapter real (DB/HR) reemplaza el seam
/// sin tocar el controller.
/// </summary>
public sealed class StubDoctorDirectory : IDoctorDirectory
{
    private readonly ConcurrentDictionary<string, MedicalDoctor> _doctors;

    public StubDoctorDirectory()
    {
        _doctors = new ConcurrentDictionary<string, MedicalDoctor>(StringComparer.Ordinal);
        foreach (var d in EhrDemoSeed.Doctors())
        {
            _doctors[d.Id] = d;
        }
    }

    public Task<IReadOnlyList<MedicalDoctor>> ListAsync(string? specialty = null, CancellationToken cancellationToken = default)
    {
        var all = _doctors.Values.OrderBy(d => d.FullName, StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(specialty))
        {
            return Task.FromResult<IReadOnlyList<MedicalDoctor>>(all.ToList());
        }

        var s = specialty.Trim();
        var matches = all
            .Where(d => d.Specialty.Contains(s, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IReadOnlyList<MedicalDoctor>>(matches);
    }

    public Task<MedicalDoctor?> GetAsync(string doctorId, CancellationToken cancellationToken = default)
        => Task.FromResult(_doctors.TryGetValue(doctorId ?? string.Empty, out var d) ? d : null);
}
