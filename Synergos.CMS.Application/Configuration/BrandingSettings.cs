namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Branding</c>. Feeds <c>DefaultBrandingProvider</c>.
/// </summary>
/// <remarks>
/// Kept deliberately minimal. New fields are added only when a concrete
/// consumer needs them (ADR 0010, ADR 0009). Binding is done by the
/// Web composer — this assembly does not reference
/// <c>Microsoft.Extensions.Options</c>; the composer extracts
/// <c>.Value</c> and passes this POCO into <c>DefaultBrandingProvider</c>.
/// </remarks>
public sealed class BrandingSettings
{
    /// <summary>Stable brand key. Default: <c>"default"</c>.</summary>
    public string Key { get; init; } = "default";

    /// <summary>Human-facing brand name. Default: <c>"Synergos"</c>.</summary>
    public string DisplayName { get; init; } = "Synergos";
}
