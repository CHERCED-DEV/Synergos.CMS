using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Presentation.Middlewares;

/// <summary>
/// Gates entire URL trees on a feature flag. For each configured
/// <see cref="FeatureGate"/>, requests whose path starts with the prefix
/// short-circuit to 404 when the named flag is <c>false</c>.
///
/// Runs before routing so disabled feature routes never hit a controller.
/// Umbraco backoffice (<c>/umbraco</c>), API (<c>/api</c>), static assets
/// (<c>/App_Plugins</c>, <c>/css</c>, <c>/js</c>) and health endpoints
/// (<c>/healthz</c>, <c>/readyz</c>) are intentionally untouched — gating
/// those via flags would break admin access when a flag is off.
/// </summary>
public sealed class FeatureGateMiddleware
{
    private static readonly string[] BypassPrefixes =
    [
        "/umbraco", "/api", "/App_Plugins", "/css", "/js",
        "/healthz", "/readyz", "/error"
    ];

    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<FeatureGateSettings> _options;
    private readonly IFeatureFlags _flags;
    private readonly ILogger<FeatureGateMiddleware> _logger;

    public FeatureGateMiddleware(
        RequestDelegate next,
        IOptionsMonitor<FeatureGateSettings> options,
        IFeatureFlags flags,
        ILogger<FeatureGateMiddleware> logger)
    {
        _next    = next;
        _options = options;
        _flags   = flags;
        _logger  = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (string.IsNullOrEmpty(path) || IsBypass(path))
        {
            await _next(context);
            return;
        }

        var gates = _options.CurrentValue.Gates;
        foreach (var gate in gates)
        {
            if (string.IsNullOrWhiteSpace(gate.PathPrefix) || string.IsNullOrWhiteSpace(gate.FeatureFlag))
                continue;

            if (!path.StartsWith(gate.PathPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_flags.IsEnabled(gate.FeatureFlag, defaultIfMissing: true))
                continue;

            _logger.LogInformation(
                "FeatureGate: request {Path} blocked (flag '{Flag}' is disabled).",
                path, gate.FeatureFlag);
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }

    private static bool IsBypass(string path)
    {
        foreach (var prefix in BypassPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
