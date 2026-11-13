using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary><see cref="IOrdenesMedicas"/> sobre el almacén PHI cifrado (003-salud-cita, I4).</summary>
public sealed class FileSystemOrdenesMedicas : IOrdenesMedicas
{
    private const string Ordenes = "clinical-orders";
    private const string Resultados = "lab-results";

    private readonly IPhiStore _store;

    public FileSystemOrdenesMedicas(IPhiStore store) => _store = store;

    public Task<OrdenMedica?> GetOrdenAsync(Guid id, CancellationToken cancellationToken) => Leer<OrdenMedica>(Ordenes, id, cancellationToken);

    public async Task<IReadOnlyList<OrdenMedica>> ListOrdenesAsync(Guid patientKey, CancellationToken cancellationToken)
        => (await Todos<OrdenMedica>(Ordenes, cancellationToken).ConfigureAwait(false))
            .Where(o => o.PatientKey == patientKey).OrderByDescending(o => o.CreadaUtc).ToList();

    public Task GuardarOrdenAsync(OrdenMedica orden, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orden);
        return _store.WriteAsync(Ordenes, orden.Id, JsonSerializer.Serialize(orden), cancellationToken);
    }

    public Task<ResultadoDeExamen?> GetResultadoAsync(Guid id, CancellationToken cancellationToken) => Leer<ResultadoDeExamen>(Resultados, id, cancellationToken);

    public async Task<IReadOnlyList<ResultadoDeExamen>> ListResultadosAsync(Guid patientKey, CancellationToken cancellationToken)
        => (await Todos<ResultadoDeExamen>(Resultados, cancellationToken).ConfigureAwait(false))
            .Where(r => r.PatientKey == patientKey).OrderByDescending(r => r.CargadoUtc).ToList();

    public Task GuardarResultadoAsync(ResultadoDeExamen resultado, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        return _store.WriteAsync(Resultados, resultado.Id, JsonSerializer.Serialize(resultado), cancellationToken);
    }

    private async Task<T?> Leer<T>(string tipo, Guid id, CancellationToken ct) where T : class
    {
        var json = await _store.ReadAsync(tipo, id, ct).ConfigureAwait(false);
        return json is null ? null : JsonSerializer.Deserialize<T>(json);
    }

    private async Task<IReadOnlyList<T>> Todos<T>(string tipo, CancellationToken ct) where T : class
        => (await _store.ListAsync(tipo, ct).ConfigureAwait(false))
            .Select(json => JsonSerializer.Deserialize<T>(json)).OfType<T>().ToList();
}
