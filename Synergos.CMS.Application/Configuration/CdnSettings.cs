namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Cdn</c>. Currently carries the allowlist consumed by
/// the <c>elementIntScriptHost</c> renderer; future fields (CDN mode
/// switch, registry base URL, etc.) land here as ADR 0012 evolves.
/// </summary>
/// <remarks>
/// <para>
/// <c>AllowedScriptOrigins</c> is an array of origins (scheme + host
/// + optional port, e.g. <c>https://www.googletagmanager.com</c>).
/// The ScriptHost renderer validates every editor-authored
/// <c>scriptSrc</c> against this list before emitting the
/// <c>&lt;script&gt;</c> tag — URL whose origin does not match gets
/// replaced with an HTML comment and a Serilog warning.
/// </para>
/// <para>
/// Default is an empty array: a deploy that opts in to scripts must
/// explicitly list every approved origin. Fail-safe by default.
/// </para>
/// </remarks>
public sealed class CdnSettings
{
    /// <summary>
    /// Whitelisted origins for <c>elementIntScriptHost</c>. Each entry
    /// is a scheme + host[:port] string, case-insensitive match.
    /// Example: <c>"https://www.googletagmanager.com"</c>.
    /// </summary>
    public string[] AllowedScriptOrigins { get; init; } = Array.Empty<string>();
}
