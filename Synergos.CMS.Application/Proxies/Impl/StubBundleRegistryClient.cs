using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Proxies.Impl;

/// <summary>
/// Placeholder implementation of <see cref="IBundleRegistryClient"/>
/// that always resolves to <c>null</c>. Used while the CDN team has
/// not yet published the registry contract.
/// </summary>
/// <remarks>
/// Per ADR 0012 the CMS consumes — but does not own — the CDN bundle
/// registry. Until the CDN team publishes the real contract, this
/// stub exists so that:
/// <list type="bullet">
///   <item>Consumers can type-check against
///   <see cref="IBundleRegistryClient"/> without waiting for the
///   adapter.</item>
///   <item>Tests that need a non-resolving client have a trivial
///   implementation to inject.</item>
/// </list>
///
/// Deliberate omissions:
/// <list type="bullet">
///   <item><b>No logging</b>. Adding <see cref="ILogger{T}"/> would
///   pull <c>Microsoft.Extensions.Logging.Abstractions</c> into
///   <c>Synergos.CMS.Application</c>, which neither ADR 0002 nor the
///   current scope of Ola 6 authorise.</item>
///   <item><b>Not registered in DI</b>. Registration (if ever) must
///   be gated on <c>Synergos:CDN:Mode = "stub"</c> and confined to
///   Development, per the guardrail in ADR 0012. The wiring lives in
///   a composer and is added when the first real consumer arrives
///   (Ola 7+). See <c>Synergos.CMS.Web/docs/umbraco/cdn-contract.md</c>.</item>
/// </list>
/// </remarks>
public sealed class StubBundleRegistryClient : IBundleRegistryClient
{
    public Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default)
        => Task.FromResult<BundleDescriptor?>(null);
}
