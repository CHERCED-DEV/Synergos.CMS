using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary><see cref="INotasDeLaConsulta"/> sobre el almacén PHI cifrado (003-salud-cita, I3 P6).</summary>
public sealed class FileSystemNotasDeLaConsulta : INotasDeLaConsulta
{
    private const string ResourceType = "consult-notes";

    private readonly IPhiStore _store;

    public FileSystemNotasDeLaConsulta(IPhiStore store) => _store = store;

    public async Task<NotaDeLaConsulta?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, id, cancellationToken).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<NotaDeLaConsulta>(json);
    }

    public async Task<IReadOnlyList<NotaDeLaConsulta>> ListAsync(Guid patientKey, CancellationToken cancellationToken)
    {
        var todas = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
        return todas
            .Select(json => JsonSerializer.Deserialize<NotaDeLaConsulta>(json))
            .Where(n => n is not null && n.PatientKey == patientKey)
            .Select(n => n!)
            .OrderByDescending(n => n.CreadaUtc)
            .ToList();
    }

    public Task GuardarAsync(NotaDeLaConsulta nota, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nota);
        return _store.WriteAsync(ResourceType, nota.Id, JsonSerializer.Serialize(nota), cancellationToken);
    }
}
