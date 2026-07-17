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
///   <item><b>Registered in DI since Ola 8.5</b> as the default
///   <see cref="IBundleRegistryClient"/> implementation in
///   <c>SeamComposer</c>. Registration is currently unconditional
///   (dev-time behaviour); a <c>Synergos:Cdn:Mode</c> switch that
///   selects between stub and a future <c>HttpBundleRegistryClient</c>
///   arrives with ADR 0016 in Ola 12.5. See
///   <c>Synergos.CMS.Web/docs/umbraco/cdn-contract.md</c>.</item>
/// </list>
/// </remarks>
public sealed class StubBundleRegistryClient : IBundleRegistryClient
{
    public Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default)
        => Task.FromResult<BundleDescriptor?>(null);
}
