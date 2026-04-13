namespace Synergos.CMS.Configuration;

/// <summary>
/// Strongly-typed configuration for the Flow Engine integration.
/// Bound from appsettings.json → Synergos:FlowEngine
/// </summary>
public sealed class FlowEngineSettings
{
    public const string SectionPath = "Synergos:FlowEngine";

    /// <summary>
    /// HMAC-SHA256 secret shared with Synergos.API.
    /// Must match FlowEngine:WebhookSecret in Synergos.API appsettings.json.
    /// </summary>
    public string WebhookSecret { get; init; } = "change-me-in-production";

    /// <summary>
    /// Default timeout in milliseconds for webhook HTTP calls from CMS to Synergos.API.
    /// </summary>
    public int WebhookTimeoutMs { get; init; } = 10_000;
}
