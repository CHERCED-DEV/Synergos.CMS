namespace Synergos.CMS.Interfaces;

/// <summary>
/// Resolves the active brand identity for the current request / context.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory) and ADR 0010
/// (Branding via provider, no conditional branching). The default
/// implementation <c>DefaultBrandingProvider</c> reads
/// <c>BrandingSettings</c> from configuration; alternative providers
/// (per-host, per-tenant, per-cookie) live in the future custom layer.
/// Consumers of this seam MUST NOT branch on <c>BrandIdentity.Key</c>
/// (e.g. <c>if (brand.Key == "foo")</c>). If a consumer needs
/// brand-dependent data, the data goes into <see cref="BrandIdentity"/>
/// itself or into a custom provider — never into control flow.
/// </remarks>
public interface IBrandingProvider
{
    /// <summary>Returns the brand identity active for the current context.</summary>
    BrandIdentity GetCurrent();
}

/// <summary>
/// Immutable identity of the active brand. Grow this record only when a
/// concrete consumer needs the new field; never add fields speculatively.
/// </summary>
/// <param name="Key">Stable brand key (e.g. <c>"default"</c>).</param>
/// <param name="DisplayName">Human-facing brand name.</param>
public sealed record BrandIdentity(string Key, string DisplayName);
