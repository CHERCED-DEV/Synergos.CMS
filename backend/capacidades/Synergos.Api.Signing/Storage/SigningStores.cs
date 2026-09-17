using Microsoft.Extensions.Options;
using Synergos.Api.Signing.Domain;
using Synergos.Shared;

namespace Synergos.Api.Signing.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class SigningStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "signing");
}

public interface ISigningKeyStore
{
    SigningKey? Find(string id);
    SigningKey? FindActive(string purpose);
    IReadOnlyList<SigningKey> ForPurpose(string purpose);
    void Put(SigningKey key);
}

public sealed class FileSystemSigningKeyStore : ISigningKeyStore
{
    private readonly JsonCollectionStore<SigningKey> _store;

    public FileSystemSigningKeyStore(IOptions<SigningStorageOptions> options)
        => _store = new JsonCollectionStore<SigningKey>(options.Value.Root, "keys", k => k.Id);

    public SigningKey? Find(string id) => _store.Find(id);

    /// <summary>La llave vigente de ese propósito: la más nueva que todavía puede emitir.</summary>
    public SigningKey? FindActive(string purpose)
        => _store.Where(k => string.Equals(k.Purpose, purpose, StringComparison.OrdinalIgnoreCase) && k.CanSign)
            .OrderByDescending(k => k.CreatedAtUtc)
            .ThenBy(k => k.Id, StringComparer.Ordinal)
            .FirstOrDefault();

    public IReadOnlyList<SigningKey> ForPurpose(string purpose)
        => _store.Where(k => string.Equals(k.Purpose, purpose, StringComparison.OrdinalIgnoreCase));

    public void Put(SigningKey key) => _store.Put(key);
}
