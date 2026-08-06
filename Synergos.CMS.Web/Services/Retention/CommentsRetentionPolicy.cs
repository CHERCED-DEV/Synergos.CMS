using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retention para comments rejected. Diferente al resto: NO elimina
/// archivos JSON (active comments aprobados pueden coexistir en el
/// mismo file). En cambio, lee cada <c>{nodeId}.json</c>, filtra
/// comments con <c>Approved=false</c> + <c>CreatedAtUtc</c> &lt;
/// cutoff, y reescribe el JSON. Cap-270 Batch B (Ola 274).
/// </summary>
/// <remarks>
/// Comments approved nunca se purgan automáticamente — son content
/// público de valor editorial. Solo rejected/spam. Si el operador
/// quiere purgar approved viejos también, hacerlo manualmente o
/// agregar setting separado.
/// </remarks>
public sealed class CommentsRetentionPolicy : IRetentionPolicy
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<CommentsSettings> _commentsSettings;
    private readonly IOptionsMonitor<RetentionSettings> _retentionSettings;
    private readonly ILogger<CommentsRetentionPolicy> _logger;

    public CommentsRetentionPolicy(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<CommentsSettings> commentsSettings,
        IOptionsMonitor<RetentionSettings> retentionSettings,
        ILogger<CommentsRetentionPolicy> logger)
    {
        _hostEnvironment = hostEnvironment;
        _commentsSettings = commentsSettings;
        _retentionSettings = retentionSettings;
        _logger = logger;
    }

    public string Name => "comments";

    public async Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _retentionSettings.CurrentValue.CommentsRejectedRetentionDays;
        if (retentionDays <= 0) return 0;

        var root = Path.Combine(_hostEnvironment.ContentRootPath,
            _commentsSettings.CurrentValue.StorageRoot);
        if (!Directory.Exists(root)) return 0;

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var purged = 0;

        foreach (var path in Directory.EnumerateFiles(root, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            List<Comment>? comments;
            try
            {
                var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                comments = JsonSerializer.Deserialize<List<Comment>>(bytes, SerializerOptions);
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _logger.LogWarning(ex, "Skipping unreadable comments file {File}", path);
                continue;
            }
            if (comments is null || comments.Count == 0) continue;

            var before = comments.Count;
            comments.RemoveAll(c => !c.Approved && c.CreatedAtUtc < cutoff);
            var removed = before - comments.Count;
            if (removed > 0)
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(comments, SerializerOptions);
                await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                purged += removed;
            }
        }
        return purged;
    }
}
