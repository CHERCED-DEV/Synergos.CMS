using Microsoft.Extensions.Options;
using Synergos.Api.Identity.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Identity.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class IdentityStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "identity");
}

public interface IPrincipalStore
{
    Principal? Find(string id);
    Principal? FindBySubject(Ref subject);
    IReadOnlyList<Principal> All();
    void Put(Principal principal);
}

public sealed class FileSystemPrincipalStore : IPrincipalStore
{
    private readonly JsonCollectionStore<Principal> _store;

    public FileSystemPrincipalStore(IOptions<IdentityStorageOptions> options)
        => _store = new JsonCollectionStore<Principal>(options.Value.Root, "principals", p => p.Id);

    public Principal? Find(string id) => _store.Find(id);

    public Principal? FindBySubject(Ref subject)
    {
        // Barrido lineal, y está bien mientras el volumen sea el de v1. El día que no lo esté,
        // se cambia esta clase y nadie afuera se entera — el contrato es HTTP.
        var coincidencias = _store.Where(p => p.Subject == subject);
        return coincidencias.Count > 0 ? coincidencias[0] : null;
    }

    public IReadOnlyList<Principal> All() => _store.All();

    public void Put(Principal principal) => _store.Put(principal);
}
