using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemEncryptedPhiStore"/> (ADR 0098). Verifica
/// round-trip cifrado, que el disco NO guarde plaintext, list/delete, missing y
/// el fallback legacy de texto plano.
/// </summary>
public sealed class FileSystemEncryptedPhiStoreTests : IDisposable
{
    private const string Rt = "patients";
    private readonly string _tempRoot;
    private readonly FileSystemEncryptedPhiStore _sut;

    public FileSystemEncryptedPhiStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-phi-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _sut = new FileSystemEncryptedPhiStore(
            new StubHostEnvironment(_tempRoot),
            new EphemeralDataProtectionProvider(),
            NullLogger<FileSystemEncryptedPhiStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private string PhiPath(Guid key) =>
        Path.Combine(_tempRoot, "App_Data", "syn-healthcare", Rt, key.ToString("N") + ".phi");

    [Fact]
    public async Task Write_Then_Read_RoundTrips()
    {
        var key = Guid.NewGuid();
        await _sut.WriteAsync(Rt, key, "{\"dx\":\"hipertensión\"}", CancellationToken.None);

        var json = await _sut.ReadAsync(Rt, key, CancellationToken.None);

        Assert.Equal("{\"dx\":\"hipertensión\"}", json);
    }

    [Fact]
    public async Task Write_FileOnDisk_IsEncrypted_NotPlaintext()
    {
        var key = Guid.NewGuid();
        await _sut.WriteAsync(Rt, key, "DIAGNOSTICO_SECRETO_12345", CancellationToken.None);

        var raw = await File.ReadAllTextAsync(PhiPath(key));
        Assert.DoesNotContain("DIAGNOSTICO_SECRETO_12345", raw);   // cifrado at-rest
    }

    [Fact]
    public async Task Read_Missing_ReturnsNull()
    {
        Assert.Null(await _sut.ReadAsync(Rt, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task List_ReturnsAllDecrypted()
    {
        await _sut.WriteAsync(Rt, Guid.NewGuid(), "{\"a\":1}", CancellationToken.None);
        await _sut.WriteAsync(Rt, Guid.NewGuid(), "{\"b\":2}", CancellationToken.None);

        var all = await _sut.ListAsync(Rt, CancellationToken.None);

        Assert.Equal(2, all.Count);
        Assert.Contains("{\"a\":1}", all);
        Assert.Contains("{\"b\":2}", all);
    }

    [Fact]
    public async Task List_UnknownResourceType_Empty()
    {
        Assert.Empty(await _sut.ListAsync("nope", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_RemovesFile()
    {
        var key = Guid.NewGuid();
        await _sut.WriteAsync(Rt, key, "{}", CancellationToken.None);

        Assert.True(await _sut.DeleteAsync(Rt, key, CancellationToken.None));
        Assert.Null(await _sut.ReadAsync(Rt, key, CancellationToken.None));
        Assert.False(await _sut.DeleteAsync(Rt, key, CancellationToken.None));   // idempotente
    }

    [Fact]
    public async Task Read_UndecryptableContent_ReturnsNull()
    {
        var key = Guid.NewGuid();
        var path = PhiPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "{\"plano\":true}");   // texto plano, no cifrado

        // Fail-safe: contenido no descifrable NO se devuelve crudo (no fuga de PHI).
        Assert.Null(await _sut.ReadAsync(Rt, key, CancellationToken.None));
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
