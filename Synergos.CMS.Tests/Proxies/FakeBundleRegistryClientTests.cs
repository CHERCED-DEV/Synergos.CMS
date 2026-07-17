using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Proxies;

public class FakeBundleRegistryClientTests
{
    [Fact]
    public async Task DefaultConstructor_ReturnsNull()
    {
        var fake = new FakeBundleRegistryClient();

        Assert.Null(await fake.TryResolveAsync("anything"));
    }

    [Fact]
    public async Task FixedResult_ReturnsTheSameDescriptorRegardlessOfKey()
    {
        var descriptor = new BundleDescriptor(
            new Uri("https://cdn.example/main.js"),
            Array.Empty<Uri>(),
            "1.0.0");
        var fake = new FakeBundleRegistryClient(descriptor);

        Assert.Same(descriptor, await fake.TryResolveAsync("key-one"));
        Assert.Same(descriptor, await fake.TryResolveAsync("key-two"));
    }

    [Fact]
    public async Task ResolverFunction_DispatchesByKey()
    {
        var known = new BundleDescriptor(
            new Uri("https://cdn.example/known.js"),
            Array.Empty<Uri>(),
            "2.0.0");
        var fake = new FakeBundleRegistryClient(
            key => key == "known" ? known : null);

        Assert.Same(known, await fake.TryResolveAsync("known"));
        Assert.Null(await fake.TryResolveAsync("unknown"));
    }
}
