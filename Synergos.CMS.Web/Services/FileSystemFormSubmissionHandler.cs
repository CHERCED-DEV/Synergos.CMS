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
public sealed class FileSystemFormSubmissionHandler : IFormSubmissionHandler
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
