using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemBundleRegistryClient"/> (Cap-280
/// Batch B, Ola 286). Cubre boot loading, lookup triple (tag/alias/
/// name), framework/slot fallback, SRI lazy compute, degradación
/// graceful, y graceful disposal.
/// </summary>
public sealed class FileSystemBundleRegistryClientTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _bundlesRoot;
    private readonly List<FileSystemBundleRegistryClient> _clientsToDispose = new();

    public FileSystemBundleRegistryClientTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-cdn-test-" + Guid.NewGuid().ToString("N"));
        _bundlesRoot = Path.Combine(_tempRoot, "synergos");
        Directory.CreateDirectory(_bundlesRoot);
    }

    public void Dispose()
    {
        foreach (var c in _clientsToDispose)
        {
            try { c.Dispose(); } catch { /* best-effort */ }
        }
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private FileSystemBundleRegistryClient BuildClient(BundleRegistrySettings? settings = null)
    {
        var s = settings ?? new BundleRegistrySettings
        {
            Mode = "FileSystem",
            LocalPath = _tempRoot,
            BundlesNamespace = "synergos",
            RegistryFileName = "registry.json",
            PublicBaseUrl = "/cdn-bundles",
            DefaultFramework = "angular",
            DefaultSlot = "latest",
            ComputeIntegrityIfMissing = true,
            HotReloadEnabled = false, // disabled en tests para evitar timer races
        };
        var monitor = Substitute.For<IOptionsMonitor<BundleRegistrySettings>>();
        monitor.CurrentValue.Returns(s);
        var client = new FileSystemBundleRegistryClient(
            monitor,
            NullLogger<FileSystemBundleRegistryClient>.Instance);
        _clientsToDispose.Add(client);
        return client;
    }

    private void WriteRegistry(string content) =>
        System.IO.File.WriteAllText(Path.Combine(_bundlesRoot, "registry.json"), content);

    private void WriteManifest(string folder, string framework, string slot, string content)
    {
        var dir = Path.Combine(_bundlesRoot, folder, framework, slot);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllText(Path.Combine(dir, "manifest.json"), content);
    }

    private void WriteMainJs(string folder, string framework, string slot, string body)
    {
        var dir = Path.Combine(_bundlesRoot, folder, framework, slot);
        Directory.CreateDirectory(dir);
        System.IO.File.WriteAllBytes(Path.Combine(dir, "main.js"), Encoding.UTF8.GetBytes(body));
    }

    private const string SimpleRegistry = """
    {
      "generated": "2026-04-29T10:00:00Z",
      "version": "1.0.0",
      "baseUrl": "/synergos",
      "elements": [
        {
          "name": "column",
          "alias": "elementStructColumn",
          "tag": "synergos-column",
          "tier": "primitive",
          "implementations": {
            "angular": { "latest": "0.1.0", "v0": "0.1.0" }
          }
        },
        {
          "name": "accordion",
          "alias": "elementCompAccordion",
          "tag": "synergos-accordion",
          "tier": "composition",
          "implementations": {
            "svelte": { "latest": "0.1.0", "v0": "0.1.0" }
          }
        }
      ]
    }
    """;

    private const string SimpleManifestWithoutIntegrity = """
    {
      "tag": "synergos-column",
      "alias": "elementStructColumn",
      "framework": "angular",
      "version": "0.1.0",
      "tier": "primitive",
      "entryScript": "main.js"
    }
    """;

    [Fact]
    public async Task ResolveByTag_HappyPath_ReturnsDescriptorWithUrlAndIntegrity()
    {
        WriteRegistry(SimpleRegistry);
        WriteManifest("column", "angular", "latest", SimpleManifestWithoutIntegrity);
        WriteMainJs("column", "angular", "latest", "console.log('column');");

        var client = BuildClient();
        var d = await client.TryResolveAsync("synergos-column");

        Assert.NotNull(d);
        Assert.Equal("synergos-column", d!.Tag);
        Assert.Equal("elementStructColumn", d.Alias);
        Assert.Equal("primitive", d.Tier);
        Assert.Equal("angular", d.Framework);
        Assert.Equal("0.1.0", d.Version);
        Assert.Equal("/cdn-bundles/synergos/column/angular/latest/main.js",
            d.MainEntryUri.ToString());
        Assert.NotNull(d.Integrity);
        Assert.StartsWith("sha384-", d.Integrity);
    }

    [Fact]
    public async Task ResolveByAlias_FindsElement()
    {
        WriteRegistry(SimpleRegistry);
        WriteManifest("column", "angular", "latest", SimpleManifestWithoutIntegrity);
        WriteMainJs("column", "angular", "latest", "x");

        var client = BuildClient();
        var d = await client.TryResolveAsync("elementStructColumn");

        Assert.NotNull(d);
        Assert.Equal("synergos-column", d!.Tag);
    }

    [Fact]
    public async Task ResolveByName_FindsElement()
    {
        WriteRegistry(SimpleRegistry);
        WriteManifest("column", "angular", "latest", SimpleManifestWithoutIntegrity);
        WriteMainJs("column", "angular", "latest", "x");

        var client = BuildClient();
        var d = await client.TryResolveAsync("column");

        Assert.NotNull(d);
        Assert.Equal("synergos-column", d!.Tag);
    }

    [Fact]
    public async Task UnknownTag_ReturnsNull()
    {
        WriteRegistry(SimpleRegistry);

        var client = BuildClient();
        var d = await client.TryResolveAsync("synergos-nonexistent");

        Assert.Null(d);
    }

    [Fact]
    public async Task RegistryMissing_GracefulDegradation()
    {
        // No registry.json escrito. El client booteа sin snapshot.
        var client = BuildClient();
        var d = await client.TryResolveAsync("synergos-column");

        Assert.Null(d);
    }

    [Fact]
    public async Task ManifestMissing_TagReturnsNull_OthersStillWork()
    {
        WriteRegistry(SimpleRegistry);
        // Solo escribimos el manifest del column, NO del accordion.
        WriteManifest("column", "angular", "latest", SimpleManifestWithoutIntegrity);
        WriteMainJs("column", "angular", "latest", "x");

        var client = BuildClient();
        var column = await client.TryResolveAsync("synergos-column");
        var accordion = await client.TryResolveAsync("synergos-accordion");

        Assert.NotNull(column);
        Assert.Null(accordion); // manifest missing → null para ese tag
    }

    [Fact]
    public async Task FrameworkFallback_WhenDefaultNotAvailable()
    {
        // Registry tiene accordion solo con svelte (no angular default).
        WriteRegistry(SimpleRegistry);
        WriteManifest("accordion", "svelte", "latest", """
        { "tag": "synergos-accordion", "framework": "svelte", "version": "0.1.0", "entryScript": "main.js" }
        """);
        WriteMainJs("accordion", "svelte", "latest", "x");

        var client = BuildClient();
        var d = await client.TryResolveAsync("synergos-accordion");

        Assert.NotNull(d);
        Assert.Equal("svelte", d!.Framework);
        Assert.Contains("/svelte/", d.MainEntryUri.ToString());
    }

    [Fact]
    public async Task ManifestWithIntegrity_PassesThroughWithoutCompute()
    {
        WriteRegistry(SimpleRegistry);
        WriteManifest("column", "angular", "latest", """
        {
          "tag": "synergos-column",
          "framework": "angular",
          "version": "0.1.0",
          "entryScript": "main.js",
          "integrity": "sha384-PRE_PROVIDED_HASH"
        }
        """);
        WriteMainJs("column", "angular", "latest", "x");

        var client = BuildClient();
        var d = await client.TryResolveAsync("synergos-column");

        Assert.NotNull(d);
        Assert.Equal("sha384-PRE_PROVIDED_HASH", d!.Integrity);
    }

    [Fact]
    public async Task ComputeIntegrity_DisabledViaSetting_ReturnsNull()
    {
        WriteRegistry(SimpleRegistry);
        WriteManifest("column", "angular", "latest", SimpleManifestWithoutIntegrity);
        WriteMainJs("column", "angular", "latest", "x");

        var client = BuildClient(new BundleRegistrySettings
        {
            Mode = "FileSystem",
            LocalPath = _tempRoot,
            BundlesNamespace = "synergos",
            RegistryFileName = "registry.json",
            PublicBaseUrl = "/cdn-bundles",
            DefaultFramework = "angular",
            DefaultSlot = "latest",
            ComputeIntegrityIfMissing = false,
            HotReloadEnabled = false,
        });
        var d = await client.TryResolveAsync("synergos-column");

        Assert.NotNull(d);
        Assert.Null(d!.Integrity);
    }

    [Fact]
    public async Task ModeStub_NoOpBoot()
    {
        // Mode != FileSystem → el ctor logueа y NO carga nada.
        var client = BuildClient(new BundleRegistrySettings
        {
            Mode = "Stub",
            LocalPath = _tempRoot,
        });
        WriteRegistry(SimpleRegistry); // se ignora porque mode es Stub
        var d = await client.TryResolveAsync("synergos-column");

        Assert.Null(d);
    }
}
