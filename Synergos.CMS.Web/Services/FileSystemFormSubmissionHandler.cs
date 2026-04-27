using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación por defecto de <see cref="IFormSubmissionHandler"/>
/// que persiste cada submission como un archivo JSON bajo
/// <c>{ContentRoot}/{FormsSettings.StorageRoot}/{formKey}/{timestamp}_{guid}.json</c>.
/// </summary>
/// <remarks>
/// <para>
/// El path está fuera de <c>wwwroot</c> por defecto (App_Data/) — los
/// archivos no son servidos por el static file middleware. El operador
/// los procesa offline: importar a CRM, enviar por email batch,
/// auditoría manual.
/// </para>
/// <para>
/// Esta impl es deliberadamente simple. Para producción de volumen alto:
/// adapter alterno que enqueue a un broker o despache a una API. Sigue
/// la misma seam <see cref="IFormSubmissionHandler"/> sin tocar el
/// controller.
/// </para>
/// </remarks>
public sealed class FileSystemFormSubmissionHandler : IFormSubmissionHandler, IFormSubmissionReader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder
            .UnsafeRelaxedJsonEscaping,
    };

    private readonly IOptions<FormsSettings> _options;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<FileSystemFormSubmissionHandler> _logger;

    public FileSystemFormSubmissionHandler(
        IOptions<FormsSettings> options,
        IHostEnvironment environment,
        ILogger<FileSystemFormSubmissionHandler> logger)
    {
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    public async Task<FormSubmissionResult> SubmitAsync(
        FormSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var settings = _options.Value;
        var safeKey = SanitizeForPath(request.FormKey);
        if (string.IsNullOrWhiteSpace(safeKey))
        {
            return FormSubmissionResult.Fail("invalid-form-key");
        }

        var folder = Path.Combine(_environment.ContentRootPath, settings.StorageRoot, safeKey);
        var fileName = $"{request.ReceivedAtUtc:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}.json";
        var fullPath = Path.Combine(folder, fileName);

        try
        {
            Directory.CreateDirectory(folder);

            var payload = new
            {
                formKey = request.FormKey,
                receivedAtUtc = request.ReceivedAtUtc,
                clientIp = request.ClientIp,
                userAgent = request.UserAgent,
                referrer = request.Referrer,
                fields = request.Fields,
            };

            await using var stream = File.Create(fullPath);
            await JsonSerializer.SerializeAsync(stream, payload, SerializerOptions, cancellationToken);

            _logger.LogInformation(
                "Form submission persisted: formKey={FormKey} fields={FieldCount} file={File}",
                request.FormKey,
                request.Fields.Count,
                fileName);

            return FormSubmissionResult.Ok(fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex,
                "Form submission persistence failed: formKey={FormKey}",
                request.FormKey);
            return FormSubmissionResult.Fail("storage-failed");
        }
    }

    public FormSubmissionsPage GetRecent(int page, int pageSize, string? formKeyFilter = null)
    {
        var clampedPage = page < 1 ? 1 : page;
        var clampedSize = Math.Clamp(pageSize, 1, 200);
        var settings = _options.Value;
        var root = Path.Combine(_environment.ContentRootPath, settings.StorageRoot);
        if (!Directory.Exists(root))
        {
            return new FormSubmissionsPage(Array.Empty<FormSubmissionListItem>(), clampedPage, clampedSize, 0);
        }

        var formFolders = string.IsNullOrWhiteSpace(formKeyFilter)
            ? Directory.EnumerateDirectories(root)
            : new[] { Path.Combine(root, SanitizeForPath(formKeyFilter)) }.Where(Directory.Exists);

        var all = new List<FormSubmissionListItem>();
        foreach (var folder in formFolders)
        {
            var formKey = Path.GetFileName(folder);
            foreach (var path in Directory.EnumerateFiles(folder, "*.json"))
            {
                var item = TryReadSummary(path, formKey);
                if (item is not null) all.Add(item);
            }
        }

        var total = all.Count;
        var ordered = all
            .OrderByDescending(i => i.ReceivedAtUtc)
            .Skip((clampedPage - 1) * clampedSize)
            .Take(clampedSize)
            .ToList();

        return new FormSubmissionsPage(ordered, clampedPage, clampedSize, total);
    }

    public IReadOnlyList<string> ListFormKeys()
    {
        var settings = _options.Value;
        var root = Path.Combine(_environment.ContentRootPath, settings.StorageRoot);
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }
        return Directory.EnumerateDirectories(root)
            .Select(d => Path.GetFileName(d))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
    }

    public FormSubmissionDetail? GetSubmission(string formKey, string storageId)
    {
        var safeKey = SanitizeForPath(formKey);
        var safeId = SanitizeForPath(storageId);
        if (string.IsNullOrWhiteSpace(safeKey) || string.IsNullOrWhiteSpace(safeId))
        {
            return null;
        }

        var settings = _options.Value;
        var path = Path.Combine(_environment.ContentRootPath, settings.StorageRoot, safeKey, safeId + ".json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            DateTime receivedAt = root.TryGetProperty("receivedAtUtc", out var ra)
                && ra.TryGetDateTime(out var dt) ? dt : File.GetCreationTimeUtc(path);

            string? clientIp = root.TryGetProperty("clientIp", out var ip) ? ip.GetString() : null;
            string? userAgent = root.TryGetProperty("userAgent", out var ua) ? ua.GetString() : null;
            string? referrer = root.TryGetProperty("referrer", out var rf) ? rf.GetString() : null;

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("fields", out var fieldsEl) && fieldsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in fieldsEl.EnumerateObject())
                {
                    fields[prop.Name] = prop.Value.ValueKind == JsonValueKind.String
                        ? prop.Value.GetString() ?? string.Empty
                        : prop.Value.ToString();
                }
            }

            return new FormSubmissionDetail(
                FormKey: formKey,
                StorageId: storageId,
                ReceivedAtUtc: receivedAt,
                ClientIp: clientIp,
                UserAgent: userAgent,
                Referrer: referrer,
                Fields: fields);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Form submission detail unreadable: formKey={FormKey} id={StorageId}",
                formKey, storageId);
            return null;
        }
    }

    private FormSubmissionListItem? TryReadSummary(string path, string formKey)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            DateTime receivedAt = root.TryGetProperty("receivedAtUtc", out var ra)
                && ra.TryGetDateTime(out var dt) ? dt : File.GetCreationTimeUtc(path);

            string? clientIp = root.TryGetProperty("clientIp", out var ip) ? ip.GetString() : null;

            int fieldCount = 0;
            if (root.TryGetProperty("fields", out var fields) && fields.ValueKind == JsonValueKind.Object)
            {
                foreach (var _ in fields.EnumerateObject()) fieldCount++;
            }

            return new FormSubmissionListItem(
                FormKey: formKey,
                StorageId: Path.GetFileNameWithoutExtension(path),
                ReceivedAtUtc: receivedAt,
                ClientIp: clientIp,
                FieldCount: fieldCount,
                StorageReference: path);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            _logger.LogWarning(ex, "Form submission file unreadable: {Path}", path);
            return null;
        }
    }

    private static string SanitizeForPath(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }
        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.Where(c => !invalid.Contains(c) && c != Path.DirectorySeparatorChar
            && c != Path.AltDirectorySeparatorChar).ToArray();
        return new string(chars).Trim();
    }
}
