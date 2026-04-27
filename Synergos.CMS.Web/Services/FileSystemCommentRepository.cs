using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación por defecto de <see cref="ICommentRepository"/> que
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
public sealed class FileSystemCommentRepository : ICommentRepository
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

        var persisted = new Comment(
            Id: Guid.NewGuid().ToString("N"),
            NodeId: comment.NodeId,
            MemberKey: comment.MemberKey,
            AuthorName: comment.AuthorName,
            Body: body.Trim(),
            CreatedAtUtc: DateTime.UtcNow,
            Approved: !settings.RequireModeration);

        existing.Add(persisted);

        var json = JsonSerializer.SerializeToUtf8Bytes(existing, SerializerOptions);
        await File.WriteAllBytesAsync(path, json, cancellationToken);

        _logger.LogInformation(
            "Comment persisted: nodeId={NodeId} commentId={CommentId} approved={Approved}",
            persisted.NodeId,
            persisted.Id,
            persisted.Approved);

        return persisted;
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
