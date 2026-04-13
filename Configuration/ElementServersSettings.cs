namespace Synergos.CMS.Configuration;

/// <summary>
/// Per-element URL overrides used during local development.
/// Consumed by <see cref="Rendering.ElementUrlResolver"/>.
///
/// Production: leave <see cref="Overrides"/> empty — bundles resolve via
/// <see cref="StaticAssetsSettings"/> + <see cref="Rendering.StaticUrlBuilder"/>.
///
/// Development: add entries like {"hero": "http://localhost:4202"} to point
/// specific elements at their Nx dev servers. For dynamic overrides without
/// a CMS restart, use the <c>__dev-servers.json</c> file next to the CDN
/// registry instead.
///
/// Bound from appsettings.json section "Synergos:ElementServers".
/// </summary>
public sealed class ElementServersSettings
{
    public const string SectionPath = "Synergos:ElementServers";

    /// <summary>Per-element overrides keyed by element name (e.g., "hero", "card").</summary>
    public Dictionary<string, string> Overrides { get; init; } = new();
}
