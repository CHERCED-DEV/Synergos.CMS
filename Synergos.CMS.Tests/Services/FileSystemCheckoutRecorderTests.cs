using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemCheckoutRecorder"/> (ADR 0097). Cubre
/// empty / happy / idempotente por OrderId / filtro por rango / input
/// inválido, con aislamiento por temp directory.
/// </summary>
public sealed class FileSystemCheckoutRecorderTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly FileSystemCheckoutRecorder _sut;

    public FileSystemCheckoutRecorderTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-orders-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        _sut = new FileSystemCheckoutRecorder(
            new StubHostEnvironment(_tempRoot),
            NullLogger<FileSystemCheckoutRecorder>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private static CheckoutCompleted Order(string id, DateTime when, decimal subtotal = 200m) =>
        new(id, new[] { new CheckoutLineItem("sku-1", 2, subtotal / 2) }, subtotal, "COP", when);

    [Fact]
    public void GetCheckouts_Empty_ReturnsEmpty()
    {
        Assert.Empty(_sut.GetCheckouts(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow));
    }

    [Fact]
    public void Record_Then_GetCheckouts_ReturnsIt()
    {
        var now = DateTime.UtcNow;
        _sut.Record(Order("o1", now, 350m));

        var found = _sut.GetCheckouts(now.AddHours(-1), now.AddHours(1));
        var order = Assert.Single(found);
        Assert.Equal("o1", order.OrderId);
        Assert.Equal(350m, order.Subtotal);
        Assert.Equal("COP", order.Currency);
        Assert.Single(order.LineItems);
    }

    [Fact]
    public void Record_SameOrderId_Idempotent()
    {
        var now = DateTime.UtcNow;
        _sut.Record(Order("dup", now));
        _sut.Record(Order("dup", now));

        Assert.Single(_sut.GetCheckouts(now.AddHours(-1), now.AddHours(1)));
    }

    [Fact]
    public void GetCheckouts_FiltersByDateRange()
    {
        var now = DateTime.UtcNow;
        _sut.Record(Order("old", now.AddDays(-3)));
        _sut.Record(Order("new", now));

        var found = _sut.GetCheckouts(now.AddDays(-1), now.AddHours(1));
        var order = Assert.Single(found);
        Assert.Equal("new", order.OrderId);
    }

    [Fact]
    public void Record_EmptyOrderId_Ignored()
    {
        _sut.Record(Order("", DateTime.UtcNow));
        Assert.Empty(_sut.GetCheckouts(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)));
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
