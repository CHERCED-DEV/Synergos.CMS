using Microsoft.Extensions.Options;
using Synergos.Bff.Salud.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Salud.Storage;

/// <summary>Dónde vive el almacén de este orquestador.</summary>
public sealed class SaludStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "bff-salud");
}

/// <summary>
/// La bitácora de sagas y sus compensaciones pendientes.
/// </summary>
/// <remarks>
/// <b>Este es el único almacén del BFF, y a propósito.</b> Un orquestador que guardara su propia
/// copia de citas, pagos o cupos tendría dos verdades que se desincronizan con las capacidades.
/// Lo único que le pertenece —y que nadie más puede saber— es <i>qué pasos dio y qué queda por
/// deshacer</i>.
/// </remarks>
public interface ISagaStore
{
    AppointmentSaga? Find(string id);
    IReadOnlyList<AppointmentSaga> WithPendingCompensations();
    IReadOnlyList<AppointmentSaga> ForPatient(Ref patient);
    void Put(AppointmentSaga saga);
}

public sealed class FileSystemSagaStore : ISagaStore
{
    private readonly JsonCollectionStore<AppointmentSaga> _store;

    public FileSystemSagaStore(IOptions<SaludStorageOptions> options)
        => _store = new JsonCollectionStore<AppointmentSaga>(options.Value.Root, "appointments", s => s.Id);

    public AppointmentSaga? Find(string id) => _store.Find(id);

    // IsUnwinding y no solo "tiene pendientes": una cita sana esperando confirmación lleva sus
    // compensaciones ARMADAS, y contarlas como pendientes llenaría la vista de operación de citas
    // que no tienen ningún problema — y haría que el barrido las ejecutara.
    public IReadOnlyList<AppointmentSaga> WithPendingCompensations()
        => _store.Where(s => s.IsUnwinding && s.Compensations.Any(c => c.IsPending))
            .OrderBy(s => s.StartedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<AppointmentSaga> ForPatient(Ref patient)
        => _store.Where(s => s.Patient == patient)
            .OrderByDescending(s => s.StartedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    public void Put(AppointmentSaga saga) => _store.Put(saga);
}
