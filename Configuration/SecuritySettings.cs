namespace Synergos.CMS.Configuration;

/// <summary>
/// Strongly-typed security configuration.
/// Bound from appsettings.json → Synergos:Security
/// </summary>
public sealed class SecuritySettings
{
    public const string SectionPath = "Synergos:Security";

    /// <summary>
    /// API key for internal endpoints (FlowOrchestrationController).
    /// Sent by callers via the <c>X-Api-Key</c> header.
    /// When empty, the filter passes through — convenient for local development.
    /// Set a strong random value in production appsettings.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;
}
