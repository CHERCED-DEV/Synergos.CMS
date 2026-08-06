using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retention para form submissions persistidas como JSON files bajo
/// <c>App_Data/syn-form-submissions/{formKey}/{timestamp}_{guid}.json</c>.
/// Usa el filename prefix <c>yyyyMMdd_HHmmss_</c> para datar; fallback
/// a <c>File.LastWriteTimeUtc</c> si filename no parsea. Cap-270
/// Batch B (Ola 274).
/// </summary>
public sealed class FormSubmissionsRetentionPolicy : IRetentionPolicy
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<FormsSettings> _formsSettings;
    private readonly IOptionsMonitor<RetentionSettings> _retentionSettings;
    private readonly ILogger<FormSubmissionsRetentionPolicy> _logger;

    public FormSubmissionsRetentionPolicy(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<FormsSettings> formsSettings,
        IOptionsMonitor<RetentionSettings> retentionSettings,
        ILogger<FormSubmissionsRetentionPolicy> logger)
    {
        _hostEnvironment = hostEnvironment;
        _formsSettings = formsSettings;
        _retentionSettings = retentionSettings;
        _logger = logger;
    }

    public string Name => "form-submissions";

    public Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _retentionSettings.CurrentValue.FormSubmissionsRetentionDays;
        if (retentionDays <= 0) return Task.FromResult(0);

        var root = Path.Combine(_hostEnvironment.ContentRootPath,
            _formsSettings.CurrentValue.StorageRoot);
        if (!Directory.Exists(root)) return Task.FromResult(0);

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var purged = 0;

        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fileDate = ResolveFileDate(file);
                if (fileDate >= cutoff) continue;
                try { File.Delete(file); purged++; }
                catch (IOException ex) { _logger.LogWarning(ex, "Failed to delete form submission {File}", file); }
            }
        }
        return Task.FromResult(purged);
    }

    private static DateTime ResolveFileDate(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        // Pattern del FileSystemFormSubmissionHandler: {yyyyMMdd_HHmmss}_{guid}
        if (name.Length >= 15 && name[8] == '_' &&
            DateTime.TryParseExact(name.Substring(0, 15), "yyyyMMdd_HHmmss",
                null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }
        return File.GetLastWriteTimeUtc(path);
    }
}
