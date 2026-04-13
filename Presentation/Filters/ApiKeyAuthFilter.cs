using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;

namespace Synergos.CMS.Presentation.Filters;

/// <summary>
/// Authorization filter that validates the <c>X-Api-Key</c> header.
/// Protects internal endpoints (FlowOrchestrationController) from unauthenticated access.
///
/// Behavior:
///   - No key configured (ApiKey = "") → request passes through with a warning (dev mode).
///   - Key configured, header missing or wrong → 401 Unauthorized.
///   - Key configured, header matches → request proceeds.
///
/// Usage: [ServiceFilter(typeof(ApiKeyAuthFilter))]
/// Registration: services.AddScoped&lt;ApiKeyAuthFilter&gt;()
/// </summary>
public sealed class ApiKeyAuthFilter : IAuthorizationFilter
{
    private const string ApiKeyHeader = "X-Api-Key";

    private readonly string?                       _configuredKey;
    private readonly ILogger<ApiKeyAuthFilter>      _logger;

    public ApiKeyAuthFilter(
        IOptions<SecuritySettings>      settings,
        ILogger<ApiKeyAuthFilter>       logger)
    {
        _configuredKey = settings.Value.ApiKey;
        _logger        = logger;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (string.IsNullOrWhiteSpace(_configuredKey))
        {
            _logger.LogWarning(
                "ApiKeyAuthFilter: Synergos:Security:ApiKey is not configured — " +
                "FlowOrchestration endpoints are open. Set a key in production appsettings.");
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue(ApiKeyHeader, out var provided)
            || !string.Equals(provided, _configuredKey, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "ApiKeyAuthFilter: rejected request from {Ip} — invalid or missing {Header}.",
                context.HttpContext.Connection.RemoteIpAddress,
                ApiKeyHeader);

            context.Result = new UnauthorizedObjectResult(new
            {
                error = $"Invalid or missing API key. Provide a valid value in the {ApiKeyHeader} header."
            });
        }
    }
}
