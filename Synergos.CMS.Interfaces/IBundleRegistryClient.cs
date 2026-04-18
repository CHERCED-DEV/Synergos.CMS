namespace Synergos.CMS.Interfaces;

/// <summary>
/// Consumes the bundle registry published by the CDN to resolve the
/// entry point and dependencies for a given element.
/// </summary>
/// <remarks>
/// Extension seam per ADR 0009 (Extension seams are mandatory) and
/// ADR 0012 (CDN contract is consumed, not owned). The CMS never
/// hardcodes the shape of CDN paths; it asks this client. Concrete
/// HTTP adapter (<c>HttpBundleRegistryClient</c>) is blocked until
/// the CDN team publishes the registry contract; meanwhile a
/// <c>StubBundleRegistryClient</c> is registered in dev and returns
/// <c>null</c>. See ADR 0012 for activation pre-requisites.
/// </remarks>
public interface IBundleRegistryClient
{
    /// <summary>
    /// Tries to resolve the bundle descriptor for the given element key.
    /// Returns <c>null</c> when the element is unknown to the registry,
    /// the registry is unavailable, or a stub is in use.
    /// </summary>
    Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default);
}

/// <summary>
/// Immutable descriptor of a CDN bundle entry. The record grows only
/// when CDN publishes new mandatory fields — never speculatively.
/// </summary>
/// <param name="MainEntryUri">Absolute URI of the main JS entry point.</param>
/// <param name="Dependencies">Absolute URIs of dependent bundles, in load order.</param>
/// <param name="Version">Opaque version string (format owned by CDN).</param>
public sealed record BundleDescriptor(
    Uri MainEntryUri,
    IReadOnlyList<Uri> Dependencies,
    string Version);
