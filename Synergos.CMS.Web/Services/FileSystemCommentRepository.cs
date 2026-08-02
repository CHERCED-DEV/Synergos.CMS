using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación por defecto de las tres caras del repositorio de comentarios
/// (<see cref="ICommentReader"/> / <see cref="ICommentWriter"/> /
/// <see cref="ICommentModeration"/>) que
/// persiste un JSON por nodo bajo
/// <c>{ContentRoot}/{CommentsSettings.StorageRoot}/{nodeId}.json</c>.
/// </summary>
/// <remarks>
/// Cero DB. Cero queue. KISS para sitios con volumen bajo/medio. El
/// fichero JSON tiene la lista completa de comments del nodo — read y
/// write son full-load + full-write. Para nodos con > 1000 comments,
/// swap por adapter sobre DB.
///
/// Concurrency: file write es atómico vía
/// <see cref="File.WriteAllBytesAsync"/>; concurrent writes al mismo
/// nodo pueden tener race conditions (un write puede sobreescribir
/// otro). Aceptable para volumen bajo. Para concurrent-heavy, lock
/// per-node.
/// </remarks>
public sealed class FileSystemCommentRepository : ICommentReader, ICommentWriter, ICommentModeration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder
            .UnsafeRelaxedJsonEscaping,
    };

    private readonly IOptions<CommentsSettings> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<FileSystemCommentRepository> _logger;

    public FileSystemCommentRepository(
        IOptions<CommentsSettings> options,
        IHostEnvironment environment,
        ILogger<FileSystemCommentRepository> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public IReadOnlyList<Comment> GetApprovedForNode(int nodeId)
    {
        var all = LoadAll(nodeId);
        return all.Where(c => c.Approved).ToList();
    }

    public IReadOnlyList<Comment> GetPendingForNode(int nodeId)
    {
        var all = LoadAll(nodeId);
        return all
            .Where(c => !c.Approved)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToList();
    }

    public IReadOnlyList<Comment> GetAllPending(int limit)
    {
        var clamped = Math.Clamp(limit, 1, 500);
        var pending = LoadAllPending(nodeIdFilter: null);
        return pending
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(clamped)
            .ToList();
    }

    public PendingCommentsPage GetPendingPage(int page, int pageSize, int? nodeIdFilter = null)
    {
        var clampedPage = page < 1 ? 1 : page;
        var clampedSize = Math.Clamp(pageSize, 1, 200);
        var pending = LoadAllPending(nodeIdFilter)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToList();
        var total = pending.Count;
        var skip = (clampedPage - 1) * clampedSize;
        var items = pending.Skip(skip).Take(clampedSize).ToList();
        return new PendingCommentsPage(items, clampedPage, clampedSize, total);
    }

    public async Task<int> BulkApproveAsync(
        IReadOnlyList<CommentRef> refs,
        CancellationToken cancellationToken)
    {
        if (refs.Count == 0) return 0;
        var byNode = refs.GroupBy(r => r.NodeId);
        var changed = 0;
        foreach (var group in byNode)
        {
            var nodeId = group.Key;
            var existing = LoadAll(nodeId);
            var ids = group.Select(r => r.CommentId).ToHashSet(StringComparer.Ordinal);
            var dirty = false;
            for (var i = 0; i < existing.Count; i++)
            {
                if (ids.Contains(existing[i].Id) && !existing[i].Approved)
                {
                    existing[i] = existing[i] with { Approved = true };
                    dirty = true;
                    changed++;
                }
            }
            if (dirty)
            {
                await PersistAsync(nodeId, existing, cancellationToken);
            }
        }
        if (changed > 0)
        {
            _logger.LogInformation("Bulk approve: {Count} comments approved", changed);
        }
        return changed;
    }

    public async Task<int> BulkRejectAsync(
        IReadOnlyList<CommentRef> refs,
        CancellationToken cancellationToken)
    {
        if (refs.Count == 0) return 0;
        var byNode = refs.GroupBy(r => r.NodeId);
        var changed = 0;
        foreach (var group in byNode)
        {
            var nodeId = group.Key;
            var existing = LoadAll(nodeId);
            var ids = group.Select(r => r.CommentId).ToHashSet(StringComparer.Ordinal);
            var removed = existing.RemoveAll(c => ids.Contains(c.Id));
            if (removed > 0)
            {
                await PersistAsync(nodeId, existing, cancellationToken);
                changed += removed;
            }
        }
        if (changed > 0)
        {
            _logger.LogInformation("Bulk reject: {Count} comments deleted", changed);
        }
        return changed;
    }

    public IReadOnlyList<Comment> ReadByRefs(IReadOnlyList<CommentRef> refs)
    {
        if (refs.Count == 0) return Array.Empty<Comment>();
        var byNode = refs.GroupBy(r => r.NodeId);
        var result = new List<Comment>();
        foreach (var group in byNode)
        {
            var nodeId = group.Key;
            var existing = LoadAll(nodeId);
            var ids = group.Select(r => r.CommentId).ToHashSet(StringComparer.Ordinal);
            result.AddRange(existing.Where(c => ids.Contains(c.Id)));
        }
        return result;
    }

    public async Task<int> RestoreAsync(
        IReadOnlyList<Comment> items,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0) return 0;
        var byNode = items.GroupBy(c => c.NodeId);
        var restored = 0;
        foreach (var group in byNode)
        {
            var nodeId = group.Key;
            var existing = LoadAll(nodeId);
            var existingIds = existing.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var toAdd = group.Where(c => !existingIds.Contains(c.Id)).ToList();
            if (toAdd.Count == 0) continue;

            existing.AddRange(toAdd);
            await PersistAsync(nodeId, existing, cancellationToken);
            restored += toAdd.Count;
        }
        if (restored > 0)
        {
            _logger.LogInformation("Restore: {Count} comments restored", restored);
        }
        return restored;
    }

    private List<Comment> LoadAllPending(int? nodeIdFilter)
    {
        var settings = _options.Value;
        var root = Path.Combine(_environment.ContentRootPath, settings.StorageRoot);
        if (!Directory.Exists(root))
        {
            return new List<Comment>();
        }

        var pending = new List<Comment>();

        if (nodeIdFilter.HasValue)
        {
            pending.AddRange(LoadAll(nodeIdFilter.Value).Where(c => !c.Approved));
            return pending;
        }

        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (!int.TryParse(fileName, out var nodeId))
            {
                continue;
            }
            pending.AddRange(LoadAll(nodeId).Where(c => !c.Approved));
        }

        return pending;
    }

    public async Task<bool> ApproveAsync(int nodeId, string commentId, CancellationToken cancellationToken)
    {
        var existing = LoadAll(nodeId);
        var index = existing.FindIndex(c => string.Equals(c.Id, commentId, StringComparison.Ordinal));
        if (index < 0)
        {
            return false;
        }
        if (existing[index].Approved)
        {
            return true;
        }

        existing[index] = existing[index] with { Approved = true };
        await PersistAsync(nodeId, existing, cancellationToken);

        _logger.LogInformation(
            "Comment approved: nodeId={NodeId} commentId={CommentId}",
            nodeId, commentId);

        return true;
    }

    public async Task<bool> RejectAsync(int nodeId, string commentId, CancellationToken cancellationToken)
    {
        var existing = LoadAll(nodeId);
        var removed = existing.RemoveAll(c => string.Equals(c.Id, commentId, StringComparison.Ordinal));
        if (removed == 0)
        {
            return false;
        }

        await PersistAsync(nodeId, existing, cancellationToken);

        _logger.LogInformation(
            "Comment rejected/deleted: nodeId={NodeId} commentId={CommentId}",
            nodeId, commentId);

        return true;
    }

    private async Task PersistAsync(int nodeId, List<Comment> comments, CancellationToken cancellationToken)
    {
        var path = ResolvePath(nodeId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.SerializeToUtf8Bytes(comments, SerializerOptions);
        await File.WriteAllBytesAsync(path, json, cancellationToken);
    }

    public async Task<Comment> AddAsync(NewComment comment, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var path = ResolvePath(comment.NodeId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var existing = LoadAll(comment.NodeId).ToList();

        var body = comment.Body ?? string.Empty;
        if (body.Length > settings.MaxBodyLengthChars)
        {
            body = body[..settings.MaxBodyLengthChars];
        }

        // Ola 230 (ADR 0100): anidación de 1 nivel. Resolvemos el parent
        // pedido contra el store del nodo y NORMALIZAMOS a top-level:
        //  - parent inexistente / no aprobado  ⇒ el nuevo es top-level.
        //  - parent es una respuesta (tiene ParentId)  ⇒ re-anclamos al
        //    abuelo top-level para no exceder 2 niveles.
        var parentId = ResolveTopLevelParent(existing, comment.ParentId);

        var persisted = new Comment(
            Id: Guid.NewGuid().ToString("N"),
            NodeId: comment.NodeId,
            MemberKey: comment.MemberKey,
            AuthorName: comment.AuthorName,
            Body: body.Trim(),
            CreatedAtUtc: DateTime.UtcNow,
            Approved: !settings.RequireModeration,
            ParentId: parentId,
            Likes: 0);

        existing.Add(persisted);

        var json = JsonSerializer.SerializeToUtf8Bytes(existing, SerializerOptions);
        await File.WriteAllBytesAsync(path, json, cancellationToken);

        _logger.LogInformation(
            "Comment persisted: nodeId={NodeId} commentId={CommentId} parentId={ParentId} approved={Approved}",
            persisted.NodeId,
            persisted.Id,
            persisted.ParentId,
            persisted.Approved);

        return persisted;
    }

    public async Task<Comment?> LikeAsync(int nodeId, string commentId, CancellationToken cancellationToken)
    {
        var existing = LoadAll(nodeId);
        var index = existing.FindIndex(c => string.Equals(c.Id, commentId, StringComparison.Ordinal));
        if (index < 0 || !existing[index].Approved)
        {
            return null;
        }

        var updated = existing[index] with { Likes = existing[index].Likes + 1 };
        existing[index] = updated;
        await PersistAsync(nodeId, existing, cancellationToken);

        _logger.LogInformation(
            "Comment liked: nodeId={NodeId} commentId={CommentId} likes={Likes}",
            nodeId, commentId, updated.Likes);

        return updated;
    }

    /// <summary>
    /// Devuelve el Id (como Guid) del comentario top-level al que debe
    /// anclarse una respuesta, o <c>null</c> si la respuesta debe quedar
    /// como top-level. Garantiza el invariante "máximo 2 niveles":
    /// si el parent pedido ya es una respuesta, sube al abuelo.
    /// </summary>
    private static Guid? ResolveTopLevelParent(List<Comment> existing, Guid? requestedParentId)
    {
        if (requestedParentId is not { } parentGuid)
        {
            return null;
        }

        var parent = existing.FirstOrDefault(c =>
            Guid.TryParseExact(c.Id, "N", out var cid) && cid == parentGuid);

        if (parent is null || !parent.Approved)
        {
            // Parent fantasma o no aprobado ⇒ degradar a top-level.
            return null;
        }

        // Parent es a su vez una respuesta ⇒ re-anclar al abuelo top-level.
        return parent.ParentId ?? parentGuid;
    }

    private List<Comment> LoadAll(int nodeId)
    {
        var path = ResolvePath(nodeId);
        if (!File.Exists(path))
        {
            return new List<Comment>();
        }

        try
        {
            var bytes = File.ReadAllBytes(path);
            var list = JsonSerializer.Deserialize<List<Comment>>(bytes, SerializerOptions);
            return list ?? new List<Comment>();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex,
                "Comments file unreadable for nodeId={NodeId} — devolviendo lista vacía.",
                nodeId);
            return new List<Comment>();
        }
    }

    private string ResolvePath(int nodeId) =>
        Path.Combine(_environment.ContentRootPath, _options.Value.StorageRoot, $"{nodeId}.json");
}
