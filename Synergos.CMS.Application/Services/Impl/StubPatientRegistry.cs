using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPatientRegistry"/> — padrón de pacientes STUB del dashboard
/// EHR-lite (OLA 5), calcando <c>StubCourseCatalogProvider</c>: sirve un padrón
/// sembrado (<see cref="EhrDemoSeed"/>) en memoria para que la demo corra
/// end-to-end sin DB. Lógica pura (ADR 0002).
/// </summary>
/// <remarks>
/// ⛔ <strong>Este seam NO admite un adapter HIS/DB real "sin tocar el controller".</strong>
/// Su consumidor (<c>EhrController</c>, <c>/api/ehr</c>) es la capa de demo y sirve
/// TODOS sus endpoints de forma anónima; eso solo es seguro mientras la data sea
/// fabricada. Sustituir esta impl por un padrón real publicaría el censo entero a
/// cualquier anónimo, con build verde y sin que el diff toque una sola línea del
/// controller. El padrón de PRODUCCIÓN es <c>IPatientRepository</c> detrás de
/// <c>/api/healthcare</c>, que gatea con <c>IPhiAccessGuard</c> (ADR 0098).
/// </remarks>
public sealed class StubPatientRegistry : IPatientRegistry
{
    private readonly ConcurrentDictionary<string, EhrPatient> _patients;

    public StubPatientRegistry()
        : this(EhrDemoSeed.Patients())
    {
    }

    /// <summary>Un padrón dado, para los tests que necesitan un caso que la siembra no trae.</summary>
    internal StubPatientRegistry(IEnumerable<EhrPatient> padron)
    {
        _patients = new ConcurrentDictionary<string, EhrPatient>(StringComparer.Ordinal);
        foreach (var p in padron)
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
        // Ignora mayúsculas Y tildes: buscar "jose garcia" debe encontrar a "José García"
        // — un clínico apurado no escribe tildes, y aquí no encontrar al paciente es grave.
        var matches = all
            .Where(p =>
                CatalogText.Contains(p.FullName, q)
                || CatalogText.Contains(p.DocumentId, q)
                || CatalogText.Contains(p.Email, q))
            .ToList();

        return Task.FromResult<IReadOnlyList<EhrPatient>>(matches);
    }

    public Task<EhrPatient?> GetAsync(string patientId, CancellationToken cancellationToken = default)
        => Task.FromResult(_patients.TryGetValue(patientId ?? string.Empty, out var p) ? p : null);

    /// <inheritdoc />
    public Task<EhrPatient?> FindByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Task.FromResult<EhrPatient?>(null);
        }

        var correo = email.Trim();
        var suyos = _patients.Values
            .Where(p => string.Equals(p.Email?.Trim(), correo, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToList();

        // UNA, o ninguna: con dos historias en el mismo correo elegir sería adivinar.
        return Task.FromResult(suyos.Count == 1 ? suyos[0] : null);
    }
}
