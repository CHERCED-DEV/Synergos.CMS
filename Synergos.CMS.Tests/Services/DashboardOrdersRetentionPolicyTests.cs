using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Retention;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="DashboardOrdersRetentionPolicy"/> (ADR 0097 D5).
/// Cubre purga de archivos viejos, no-op (0 días), directorio vacío e
/// idempotencia.
/// </summary>
public sealed class DashboardOrdersRetentionPolicyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _ordersDir;

    public DashboardOrdersRetentionPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-dash-ret-test-" + Guid.NewGuid().ToString("N"));
        _ordersDir = Path.Combine(_tempRoot, "App_Data", "syn-dashboard", "orders");
        Directory.CreateDirectory(_ordersDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private DashboardOrdersRetentionPolicy BuildSut(int retentionDays)
    {
        var monitor = Substitute.For<IOptionsMonitor<DashboardSettings>>();
        monitor.CurrentValue.Returns(new DashboardSettings { ProjectionRetentionDays = retentionDays });
        return new DashboardOrdersRetentionPolicy(
            new StubHostEnvironment(_tempRoot), monitor,
            NullLogger<DashboardOrdersRetentionPolicy>.Instance);
    }

    private void WriteOrderFile(string yyyyMMdd) =>
        File.WriteAllText(Path.Combine(_ordersDir, yyyyMMdd + ".jsonl"), "{}\n");

    [Fact]
    public void Name_IsDashboardOrders() => Assert.Equal("dashboard-orders", BuildSut(7).Name);

    [Fact]
    public async Task Sweep_DeletesOldKeepsRecent()
    {
        var today = DateTime.UtcNow.Date.ToString("yyyy-MM-dd");
        WriteOrderFile("2020-01-01");   // viejo
        WriteOrderFile(today);          // reciente

        var purged = await BuildSut(retentionDays: 7).SweepAsync(CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.False(File.Exists(Path.Combine(_ordersDir, "2020-01-01.jsonl")));
        Assert.True(File.Exists(Path.Combine(_ordersDir, today + ".jsonl")));
    }

    [Fact]
    public async Task Sweep_ZeroRetention_NoOp()
    {
        WriteOrderFile("2020-01-01");

        var purged = await BuildSut(retentionDays: 0).SweepAsync(CancellationToken.None);

        Assert.Equal(0, purged);
        Assert.True(File.Exists(Path.Combine(_ordersDir, "2020-01-01.jsonl")));
    }

    [Fact]
    public async Task Sweep_EmptyDir_ReturnsZero()
    {
        var purged = await BuildSut(retentionDays: 7).SweepAsync(CancellationToken.None);
        Assert.Equal(0, purged);
    }

    [Fact]
    public async Task Sweep_Idempotent_SecondRunPurgesNothing()
    {
        WriteOrderFile("2020-01-01");
        var sut = BuildSut(retentionDays: 7);

        var first = await sut.SweepAsync(CancellationToken.None);
        var second = await sut.SweepAsync(CancellationToken.None);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
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
