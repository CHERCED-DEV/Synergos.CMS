using Microsoft.Extensions.Options;
using Synergos.Api.Moderation.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Moderation.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class ModerationStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "moderation");
}

public interface IModerationStore
{
    ModerationCase? Find(string id);
    IReadOnlyList<ModerationCase> Queue();
    IReadOnlyList<ModerationCase> ForTarget(Ref target);
    void Put(ModerationCase item);
}

public sealed class FileSystemModerationStore : IModerationStore
{
    private readonly JsonCollectionStore<ModerationCase> _store;

    public FileSystemModerationStore(IOptions<ModerationStorageOptions> options)
        => _store = new JsonCollectionStore<ModerationCase>(options.Value.Root, "cases", c => c.Id);

    public ModerationCase? Find(string id) => _store.Find(id);

    /// <summary>La cola: lo abierto, lo más viejo primero.</summary>
    /// <remarks>
    /// Lo MÁS VIEJO primero y no lo más nuevo: con orden inverso, un pico de reportes deja los
    /// primeros al fondo para siempre y nadie los mira nunca.
    /// </remarks>
    public IReadOnlyList<ModerationCase> Queue()
        => _store.Where(c => c.Status == CaseStatus.Open)
            .OrderBy(c => c.ReportedAtUtc)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<ModerationCase> ForTarget(Ref target) => _store.Where(c => c.Target == target);

    public void Put(ModerationCase item) => _store.Put(item);
}
