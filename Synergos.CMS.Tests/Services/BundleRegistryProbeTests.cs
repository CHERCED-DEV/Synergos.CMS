using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="BundleRegistryProbe"/> (Cap-280 Batch C, Olas
/// 287-288). Verifica que el verdict del probe matchea el Mode actual:
/// Stub healthy informativo, FileSystem healthy si resuelve, FileSystem
/// unhealthy si registry no está, Unknown unhealthy.
/// </summary>
public sealed class BundleRegistryProbeTests
{
    private static IOptionsMonitor<BundleRegistrySettings> MonitorFor(BundleRegistrySettings s)
    {
        var monitor = Substitute.For<IOptionsMonitor<BundleRegistrySettings>>();
        monitor.CurrentValue.Returns(s);
        return monitor;
    }

    [Fact]
    public async Task StubMode_ReportsHealthy_WithStubMessage()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        var settings = new BundleRegistrySettings { Mode = "Stub" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.Equal("bundle_registry", result.Name);
        Assert.True(result.IsHealthy);
        Assert.Contains("stub", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        // No debe llamar al client en Stub mode — basta con el setting.
        await client.DidNotReceive().TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FileSystemMode_DescriptorResolved_ReportsHealthy()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/synergos-column/angular/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0",
            Tag: "synergos-column",
            Alias: "elementStructColumn",
            Tier: "primitive",
            Integrity: "sha384-abc123",
            Framework: "angular");
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAsync("synergos-column", Arg.Any<CancellationToken>())
              .Returns(descriptor);
        var settings = new BundleRegistrySettings { Mode = "FileSystem", LocalPath = @"C:\LOCAL_CDN" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("angular", result.Message ?? string.Empty);
        Assert.Contains("0.1.0", result.Message ?? string.Empty);
        Assert.Contains("present", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task FileSystemMode_DescriptorMissingIntegrity_ReportsHealthyWithMissingMarker()
    {
        var descriptor = new BundleDescriptor(
            MainEntryUri: new Uri("https://cdn/synergos-column/angular/latest/main.js"),
            Dependencies: Array.Empty<Uri>(),
            Version: "0.1.0",
            Framework: "angular",
            Integrity: null);
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAsync("synergos-column", Arg.Any<CancellationToken>())
              .Returns(descriptor);
        var settings = new BundleRegistrySettings { Mode = "FileSystem", LocalPath = @"C:\LOCAL_CDN" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("missing", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task FileSystemMode_NullDescriptor_ReportsUnhealthy()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns((BundleDescriptor?)null);
        var settings = new BundleRegistrySettings
        {
            Mode = "FileSystem",
            LocalPath = @"C:\LOCAL_CDN",
            BundlesNamespace = "synergos",
        };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("synergos-column", result.Message ?? string.Empty);
        Assert.Contains(@"C:\LOCAL_CDN", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task FileSystemMode_ClientThrows_ReportsUnhealthyWithExceptionMessage()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        client.TryResolveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
              .Returns<Task<BundleDescriptor?>>(_ => throw new InvalidOperationException("boom"));
        var settings = new BundleRegistrySettings { Mode = "FileSystem", LocalPath = @"C:\LOCAL_CDN" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("boom", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task UnknownMode_ReportsUnhealthy()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        var settings = new BundleRegistrySettings { Mode = "Quantum" };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("Quantum", result.Message ?? string.Empty);
    }

    [Fact]
    public async Task NullMode_DefaultsToStub_ReportsHealthy()
    {
        var client = Substitute.For<IBundleRegistryClient>();
        var settings = new BundleRegistrySettings { Mode = null! };
        var probe = new BundleRegistryProbe(client, MonitorFor(settings));

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Contains("stub", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
