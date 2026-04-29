using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services.Retention;

/// <summary>
/// Retention para <c>App_Data/syn-audit/{yyyy-MM-dd}.jsonl</c>.
/// Refactor del antiguo <c>AuditRetentionHostedService</c> a
/// <see cref="IRetentionPolicy"/>. Mantiene leer
/// <see cref="AdminSettings.AuditRetentionDays"/> como single source
/// (back-compat ADR 0070). Cap-270 Batch B (Ola 274).
/// </summary>
public sealed class AuditRetentionPolicy : IRetentionPolicy
{
    private const string DirectoryName = "syn-audit";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<AdminSettings> _adminSettings;
    private readonly ILogger<AuditRetentionPolicy> _logger;

    public AuditRetentionPolicy(
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<AdminSettings> adminSettings,
        ILogger<AuditRetentionPolicy> logger)
    {
        _hostEnvironment = hostEnvironment;
        _adminSettings = adminSettings;
        _logger = logger;
    }

    public string Name => "audit";

    public Task<int> SweepAsync(CancellationToken cancellationToken)
    {
        var retentionDays = _adminSettings.CurrentValue.AuditRetentionDays;
        if (retentionDays <= 0)
        {
            return Task.FromResult(0);
        }

        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", DirectoryName);
        if (!Directory.Exists(dir))
        {
            return Task.FromResult(0);
        }

        var cutoff = DateTime.UtcNow.Date.AddDays(-retentionDays);
        var purged = 0;

        foreach (var file in Directory.EnumerateFiles(dir, "*.jsonl"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                System.Globalization.DateTimeStyles.None, out var fileDate))
            {
                continue;
            }
            if (fileDate < cutoff)
            {
                try
                {
                    File.Delete(file);
                    purged++;
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Failed to delete audit file {File}", file);
                }
            }
        }

        return Task.FromResult(purged);
    }
}
