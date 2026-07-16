using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests de <see cref="FileSystemPaymentSessionStore"/> (T3, doc 25) — los 4 casos
/// canónicos ADR 0075 (empty / happy roundtrip / list / delete), con aislamiento por
/// temp directory. Es el store durable que hace que una sesión de pago SOBREVIVA un
/// reinicio (cierra el confirm-tras-reinicio).
/// </summary>
public sealed class FileSystemPaymentSessionStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileSystemPaymentSessionStore _sut;

    public FileSystemPaymentSessionStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-payments-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _sut = new FileSystemPaymentSessionStore(
            Options.Create(new PaymentSessionsSettings { StorageRoot = "sessions" }),
            new StubHostEnvironment(_tempRoot),
            NullLogger<FileSystemPaymentSessionStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact] // empty: sesión inexistente → null
    public async Task Read_Missing_ReturnsNull()
    {
        Assert.Null(await _sut.ReadAsync("stub_nope"));
    }

    [Fact] // happy: write → read devuelve el mismo JSON (roundtrip durable)
    public async Task Write_Then_Read_Roundtrips()
    {
        await _sut.WriteAsync("stub_abc", "{\"Status\":2,\"Amount\":1000}");
        Assert.Equal("{\"Status\":2,\"Amount\":1000}", await _sut.ReadAsync("stub_abc"));
    }

    [Fact] // filter/list: lista todas las sesiones escritas
    public async Task List_ReturnsAllWritten()
    {
        await _sut.WriteAsync("stub_1", "{\"a\":1}");
        await _sut.WriteAsync("stub_2", "{\"a\":2}");

        var all = await _sut.ListAsync();

        Assert.Equal(2, all.Count);
    }

    [Fact] // idempotent: delete quita; borrar de nuevo (o inexistente) → false, sin excepción
    public async Task Delete_RemovesAndIsIdempotent()
    {
        await _sut.WriteAsync("stub_x", "{}");

        Assert.True(await _sut.DeleteAsync("stub_x"));
        Assert.Null(await _sut.ReadAsync("stub_x"));
        Assert.False(await _sut.DeleteAsync("stub_x"));   // ya no existe
    }

    private sealed class StubHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
