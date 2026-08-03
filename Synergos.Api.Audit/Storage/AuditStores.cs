using Microsoft.Extensions.Options;
using Synergos.Api.Audit.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Audit.Storage;

/// <summary>Dónde vive el almacén de esta capacidad.</summary>
public sealed class AuditStorageOptions
{
    public string Root { get; set; } = Path.Combine(AppContext.BaseDirectory, "data", "audit");
}

/// <summary>
/// La bitácora. <b>Append-only: no hay Update ni Delete, y es la interfaz la que lo garantiza.</b>
/// </summary>
/// <remarks>
/// Una bitácora que se puede editar no sirve de bitácora: lo primero que hace quien quiere
/// esconder algo es corregir el registro. Que la operación no exista en el contrato es más fuerte
/// que un comentario pidiendo que no se use.
/// </remarks>
public interface IAuditStore
{
    AuditEntry? Find(string id);
    IReadOnlyList<AuditEntry> Query(Ref? target, string? actorId, TimeWindow window);
    void Append(AuditEntry entry);
}

public sealed class FileSystemAuditStore : IAuditStore
{
    private readonly JsonCollectionStore<AuditEntry> _store;

    public FileSystemAuditStore(IOptions<AuditStorageOptions> options)
        => _store = new JsonCollectionStore<AuditEntry>(options.Value.Root, "entries", e => e.Id);

    public AuditEntry? Find(string id) => _store.Find(id);

    public IReadOnlyList<AuditEntry> Query(Ref? target, string? actorId, TimeWindow window)
        // TimeWindow y no dos DateTimeOffset sueltos: dos parámetros del mismo tipo se pasan al
        // revés, el compilador no dice nada, y el síntoma es una bitácora que "no tiene nada".
        => _store.Where(e =>
                window.Contains(e.AtUtc)
                && (target is null || e.Target == target)
                && (string.IsNullOrEmpty(actorId) || e.Actor.Principal.Id == actorId))
            .OrderByDescending(e => e.AtUtc)
            // Desempate estable: sin él, dos consultas iguales devuelven órdenes distintos y una
            // investigación que se relee parece haber cambiado.
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();

    public void Append(AuditEntry entry) => _store.Put(entry);
}
