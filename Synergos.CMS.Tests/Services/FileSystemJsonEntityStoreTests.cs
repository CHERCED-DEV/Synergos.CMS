using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests de <see cref="FileSystemJsonEntityStore"/> — la ÚNICA impl de persistencia JSON
/// durable del proyecto (colapsa los 4 stores dedicados de T1/T3/Booking). Los 4 casos
/// canónicos ADR 0075 (empty / happy roundtrip / list / delete) MÁS las dos propiedades
/// que el genérico introduce: aislamiento entre familias (resourceType) y el mapeo EXACTO
/// de ruta <c>syn-{resourceType}/</c> — este último es el que garantiza que los datos ya
/// escritos por los stores dedicados NO se huerfanizan.
/// </summary>
public sealed class FileSystemJsonEntityStoreTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileSystemJsonEntityStore _sut;

    public FileSystemJsonEntityStoreTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-jsonstore-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _sut = new FileSystemJsonEntityStore(
            Options.Create(new JsonEntityStoreSettings { StorageRoot = "data" }),
            new StubHostEnvironment(_tempRoot),
            NullLogger<FileSystemJsonEntityStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    [Fact] // empty: entidad inexistente → null
    public async Task Read_Missing_ReturnsNull()
    {
        Assert.Null(await _sut.ReadAsync("orders", "ord_nope"));
    }

    [Fact] // happy: write → read devuelve el mismo JSON (roundtrip durable)
    public async Task Write_Then_Read_Roundtrips()
    {
        await _sut.WriteAsync("orders", "ord_abc", "{\"Status\":1}");
        Assert.Equal("{\"Status\":1}", await _sut.ReadAsync("orders", "ord_abc"));
    }

    [Fact] // filter/list: lista SOLO las entidades de esa familia
    public async Task List_ReturnsOnlyThatResourceType()
    {
        await _sut.WriteAsync("orders", "ord_1", "{\"a\":1}");
        await _sut.WriteAsync("orders", "ord_2", "{\"a\":2}");
        await _sut.WriteAsync("payments", "stub_1", "{\"b\":1}");

        Assert.Equal(2, (await _sut.ListAsync("orders")).Count);
        Assert.Single(await _sut.ListAsync("payments"));
        Assert.Empty(await _sut.ListAsync("reservations"));   // familia sin datos
    }

    [Fact] // idempotent: delete quita; borrar de nuevo (o inexistente) → false, sin excepción
    public async Task Delete_RemovesAndIsIdempotent()
    {
        await _sut.WriteAsync("orders", "ord_x", "{}");

        Assert.True(await _sut.DeleteAsync("orders", "ord_x"));
        Assert.Null(await _sut.ReadAsync("orders", "ord_x"));
        Assert.False(await _sut.DeleteAsync("orders", "ord_x"));   // ya no existe
    }

    [Fact] // aislamiento: la MISMA clave en familias distintas son entidades distintas
    public async Task SameKey_DifferentResourceTypes_AreIsolated()
    {
        await _sut.WriteAsync("orders", "same-key", "\"soy-una-orden\"");
        await _sut.WriteAsync("payments", "same-key", "\"soy-un-pago\"");

        Assert.Equal("\"soy-una-orden\"", await _sut.ReadAsync("orders", "same-key"));
        Assert.Equal("\"soy-un-pago\"", await _sut.ReadAsync("payments", "same-key"));

        // Borrar en una familia no toca la otra.
        await _sut.DeleteAsync("orders", "same-key");
        Assert.Null(await _sut.ReadAsync("orders", "same-key"));
        Assert.NotNull(await _sut.ReadAsync("payments", "same-key"));
    }

    [Fact] // RUTA: {StorageRoot}/syn-{resourceType}/{key}.json — preserva EXACTO las rutas
           // que usaban los stores dedicados (syn-orders/syn-payments/...) → los datos ya
           // escritos siguen siendo legibles. Si esto cambia, se huerfanizan.
    public async Task Path_MapsTo_SynResourceTypeSubdirectory()
    {
        await _sut.WriteAsync("travel-orders", "trip_abc", "{}");

        var expected = Path.Combine(_tempRoot, "data", "syn-travel-orders", "trip_abc.json");
        Assert.True(File.Exists(expected), $"Se esperaba el archivo en {expected}");
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
