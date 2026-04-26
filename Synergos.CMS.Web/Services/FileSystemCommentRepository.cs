using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

    public async Task<Comment> AddAsync(NewComment newComment, CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var path = ResolvePath(newComment.NodeId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var existing = LoadAll(newComment.NodeId).ToList();

        var body = newComment.Body ?? string.Empty;
        if (body.Length > settings.MaxBodyLengthChars)
        {
            body = body[..settings.MaxBodyLengthChars];
        }

        var comment = new Comment(
            Id: Guid.NewGuid().ToString("N"),
            NodeId: newComment.NodeId,
            MemberKey: newComment.MemberKey,
            AuthorName: newComment.AuthorName,
            Body: body.Trim(),
            CreatedAtUtc: DateTime.UtcNow,
            Approved: !settings.RequireModeration);

        existing.Add(comment);

        var json = JsonSerializer.SerializeToUtf8Bytes(existing, SerializerOptions);
        await File.WriteAllBytesAsync(path, json, cancellationToken);

        _logger.LogInformation(
            "Comment persisted: nodeId={NodeId} commentId={CommentId} approved={Approved}",
            newComment.NodeId,
            comment.Id,
            comment.Approved);

        return comment;
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
