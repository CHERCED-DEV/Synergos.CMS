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

    /// <summary>
    /// Las que empezaron antes de <paramref name="limite"/> y <b>siguen sin cerrar</b>.
    /// </summary>
    /// <remarks>
    /// <para>Es la consulta del abandono (HU #29), y es DISTINTA de la de arriba a propósito. Una
    /// saga en <c>Running</c> lleva sus compensaciones <b>armadas</b>, no pendientes: es una
    /// operación sana esperando el paso que cuesta. Meterla en <c>WithPendingCompensations</c>
    /// haría que el barrido deshiciera compras que iban perfectamente
    /// (<c>feedback_compensation_is_data</c>).</para>
    ///
    /// <para>Lo único que la vuelve trabajo es <b>el tiempo</b>. Por eso el filtro es una fecha y
    /// no un estado de la compensación.</para>
    /// </remarks>
    IReadOnlyList<TSaga> StartedBefore(DateTimeOffset limite);

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

    // Solo `Running`: `Compensating` ya lo barre la consulta de arriba, y los estados finales no
    // tienen nada que abandonar. Una saga que ya se está deshaciendo no se "abandona" otra vez.
    public IReadOnlyList<TSaga> StartedBefore(DateTimeOffset limite)
        => _store.Where(s => s.Status == SagaStatus.Running && s.StartedAtUtc < limite)
            .OrderBy(s => s.StartedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

    public void Put(TSaga saga) => _store.Put(saga);
}
