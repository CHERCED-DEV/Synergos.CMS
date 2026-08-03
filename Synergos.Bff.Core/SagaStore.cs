using Microsoft.Extensions.Options;
using Synergos.Shared;

namespace Synergos.Bff.Core;

/// <summary>Dónde vive el almacén de un orquestador.</summary>
public sealed class SagaStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "bff");
}

/// <summary>
/// La bitácora de sagas y sus compensaciones pendientes.
/// </summary>
/// <remarks>
/// <b>Es el único almacén de un orquestador, y a propósito.</b> Un BFF que guardara su propia
/// copia de citas, pagos, pedidos o cupos tendría dos verdades que se desincronizan con las
/// capacidades. Lo único que le pertenece —y que nadie más puede saber— es <i>qué pasos dio y
/// qué queda por deshacer</i>.
/// </remarks>
public interface ISagaStore<TSaga> where TSaga : class, ISaga
{
    TSaga? Find(string id);

    /// <summary>Las sagas que están deshaciendo algo. <b>No las sanas.</b></summary>
    IReadOnlyList<TSaga> WithPendingCompensations();

    void Put(TSaga saga);
}

/// <summary>El almacén por defecto: un JSON por orquestador.</summary>
public sealed class FileSystemSagaStore<TSaga> : ISagaStore<TSaga> where TSaga : class, ISaga
{
    private readonly JsonCollectionStore<TSaga> _store;

    public FileSystemSagaStore(IOptions<SagaStorageOptions> options)
        => _store = new JsonCollectionStore<TSaga>(options.Value.Root, "sagas", s => s.Id);

    public TSaga? Find(string id) => _store.Find(id);

    // IsUnwinding y no solo "tiene pendientes": una operación sana en curso lleva sus
    // compensaciones ARMADAS, y contarlas como pendientes llenaría la vista de operación de casos
    // que no tienen ningún problema — y haría que el barrido las ejecutara.
    public IReadOnlyList<TSaga> WithPendingCompensations()
        => _store.Where(s => s.IsUnwinding() && s.Compensations.Any(c => c.IsPending))
            .OrderBy(s => s.StartedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    public void Put(TSaga saga) => _store.Put(saga);
}
