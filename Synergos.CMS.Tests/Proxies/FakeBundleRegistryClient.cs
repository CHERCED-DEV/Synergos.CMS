using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Proxies;

/// <summary>
/// Test-only fake of <see cref="IBundleRegistryClient"/>. Accepts one
/// of three construction modes: default (always returns <c>null</c>),
/// fixed result, or a resolver delegate for per-key behaviour.
/// </summary>
/// <remarks>
/// Lives under <c>Synergos.CMS.Tests/Proxies/</c> intentionally — it
/// is test infrastructure, not production code, and is not accessible
/// from the Application layer. Future test suites that need to assert
/// how consumers behave against a bundle registry use this type
/// rather than constructing their own mock each time.
/// </remarks>
public sealed class FakeBundleRegistryClient : IBundleRegistryClient
{
    private readonly Func<string, BundleDescriptor?> _resolver;

    public FakeBundleRegistryClient()
        : this(static _ => null) { }

    public FakeBundleRegistryClient(BundleDescriptor? fixedResult)
        : this(_ => fixedResult) { }

    public FakeBundleRegistryClient(Func<string, BundleDescriptor?> resolver) =>
        _resolver = resolver;

    public Task<BundleDescriptor?> TryResolveAsync(
        string elementKey,
        CancellationToken ct = default)
        => Task.FromResult(_resolver(elementKey));
}
