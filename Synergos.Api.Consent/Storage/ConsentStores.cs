using Microsoft.Extensions.Options;
using Synergos.Api.Consent.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Consent.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class ConsentStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "consent");
}

public interface IConsentStore
{
    ConsentGrant? Find(string id);
    ConsentGrant? FindLatest(Ref subject, string purpose);
    IReadOnlyList<ConsentGrant> ForSubject(Ref subject);
    void Put(ConsentGrant grant);
}

public sealed class FileSystemConsentStore : IConsentStore
{
    private readonly JsonCollectionStore<ConsentGrant> _store;

    public FileSystemConsentStore(IOptions<ConsentStorageOptions> options)
        => _store = new JsonCollectionStore<ConsentGrant>(options.Value.Root, "grants", g => g.Id);

    public ConsentGrant? Find(string id) => _store.Find(id);

    /// <summary>El más reciente para ese sujeto y propósito.</summary>
    /// <remarks>
    /// El MÁS RECIENTE y no el primero: alguien puede revocar y volver a otorgar, y quedarse con
    /// el viejo diría "revocado" sobre un permiso que la persona volvió a dar.
    /// </remarks>
    public ConsentGrant? FindLatest(Ref subject, string purpose)
        => _store.Where(g => g.Subject == subject && string.Equals(g.Purpose, purpose, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(g => g.GrantedAtUtc)
            .ThenBy(g => g.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    public IReadOnlyList<ConsentGrant> ForSubject(Ref subject) => _store.Where(g => g.Subject == subject);

    public void Put(ConsentGrant grant) => _store.Put(grant);
}
