using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary><see cref="IRenovaciones"/> sobre el almacén PHI cifrado (003-salud-cita, I5 P14).</summary>
public sealed class FileSystemRenovaciones : IRenovaciones
{
    private const string ResourceType = "renewal-requests";

    private readonly IPhiStore _store;

    public FileSystemRenovaciones(IPhiStore store) => _store = store;

    public async Task<SolicitudDeRenovacion?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, id, cancellationToken).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<SolicitudDeRenovacion>(json);
    }

    public async Task<IReadOnlyList<SolicitudDeRenovacion>> ListAsync(Guid patientKey, CancellationToken cancellationToken)
        => (await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false))
            .Select(json => JsonSerializer.Deserialize<SolicitudDeRenovacion>(json)).OfType<SolicitudDeRenovacion>()
            .Where(s => s.PatientKey == patientKey).OrderByDescending(s => s.PedidaUtc).ToList();

    public Task GuardarAsync(SolicitudDeRenovacion solicitud, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return _store.WriteAsync(ResourceType, solicitud.Id, JsonSerializer.Serialize(solicitud), cancellationToken);
    }
}
