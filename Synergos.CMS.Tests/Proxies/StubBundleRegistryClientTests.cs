using Synergos.CMS.Application.Proxies.Impl;

namespace Synergos.CMS.Tests.Proxies;

public class StubBundleRegistryClientTests
{
    [Fact]
    public async Task TryResolveAsync_ReturnsNull_ForAnyKey()
    {
        var stub = new StubBundleRegistryClient();

        Assert.Null(await stub.TryResolveAsync("some-element"));
        Assert.Null(await stub.TryResolveAsync(string.Empty));
        Assert.Null(await stub.TryResolveAsync("a/b/c"));
    }

    [Fact]
    public async Task TryResolveAsync_ReturnsNull_EvenWhenTokenCancelled()
    {
        var stub = new StubBundleRegistryClient();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The stub completes synchronously and ignores the token by
        // design (there is no I/O to cancel). Verified here so that a
        // future author who "fixes" this to throw on cancellation gets
        // a failing test and reconsiders the contract.
        Assert.Null(await stub.TryResolveAsync("x", cts.Token));
    }
}
