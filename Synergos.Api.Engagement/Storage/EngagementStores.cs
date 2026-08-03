using Microsoft.Extensions.Options;
using Modelo = Synergos.Api.Engagement.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Engagement.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class EngagementStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "engagement");
}

public interface IEngagementStore
{
    Modelo.Engagement? Find(string id);
    Modelo.Engagement? FindOf(Ref actor, Ref target, Modelo.EngagementKind kind);
    IReadOnlyList<Modelo.Engagement> ForTarget(Ref target);
    void Put(Modelo.Engagement item);
}

public sealed class FileSystemEngagementStore : IEngagementStore
{
    private readonly JsonCollectionStore<Modelo.Engagement> _store;

    public FileSystemEngagementStore(IOptions<EngagementStorageOptions> options)
        => _store = new JsonCollectionStore<Modelo.Engagement>(options.Value.Root, "engagements", e => e.Id);

    public Modelo.Engagement? Find(string id) => _store.Find(id);

    public Modelo.Engagement? FindOf(Ref actor, Ref target, Modelo.EngagementKind kind)
    {
        var hit = _store.Where(e => e.Actor == actor && e.Target == target && e.Kind == kind && e.Visible);
        return hit.Count > 0 ? hit[0] : null;
    }

    public IReadOnlyList<Modelo.Engagement> ForTarget(Ref target) => _store.Where(e => e.Target == target);

    public void Put(Modelo.Engagement item) => _store.Put(item);
}
