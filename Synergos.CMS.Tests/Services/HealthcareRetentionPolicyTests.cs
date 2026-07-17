using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Retention;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="HealthcareRetentionPolicy"/> (ADR 0098 H3). Purga
/// archivos PHI <c>.phi</c> por última modificación más allá de la retención.
/// </summary>
public sealed class HealthcareRetentionPolicyTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _phiDir;

    public HealthcareRetentionPolicyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "syn-hc-ret-test-" + Guid.NewGuid().ToString("N"));
        _phiDir = Path.Combine(_tempRoot, "App_Data", "syn-healthcare", "patients");
        Directory.CreateDirectory(_phiDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            try { Directory.Delete(_tempRoot, recursive: true); }
            catch { /* best-effort */ }
        }
    }

    private HealthcareRetentionPolicy BuildSut(int retentionDays)
    {
        var monitor = Substitute.For<IOptionsMonitor<HealthcareSettings>>();
        monitor.CurrentValue.Returns(new HealthcareSettings { RecordRetentionDays = retentionDays });
        return new HealthcareRetentionPolicy(
            new StubHostEnvironment(_tempRoot), monitor, NullLogger<HealthcareRetentionPolicy>.Instance);
    }

    private string WritePhi(string name, DateTime lastWriteUtc)
    {
        var path = Path.Combine(_phiDir, name + ".phi");
        File.WriteAllText(path, "cipher");
        File.SetLastWriteTimeUtc(path, lastWriteUtc);
        return path;
    }

    [Fact]
    public void Name_IsHealthcare() => Assert.Equal("healthcare", BuildSut(2190).Name);

    [Fact]
    public async Task Sweep_PurgesOldKeepsRecent()
    {
        var old = WritePhi("viejo", DateTime.UtcNow.AddDays(-3000));   // > 6 años
        var recent = WritePhi("reciente", DateTime.UtcNow.AddDays(-10));

        var purged = await BuildSut(retentionDays: 2190).SweepAsync(CancellationToken.None);

        Assert.Equal(1, purged);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public async Task Sweep_ZeroRetention_NoOp()
    {
        WritePhi("viejo", DateTime.UtcNow.AddDays(-3000));
        var purged = await BuildSut(retentionDays: 0).SweepAsync(CancellationToken.None);
        Assert.Equal(0, purged);
    }

    [Fact]
    public async Task Sweep_EmptyDir_ReturnsZero() =>
        Assert.Equal(0, await BuildSut(2190).SweepAsync(CancellationToken.None));

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public StubHostEnvironment(string contentRootPath) => ContentRootPath = contentRootPath;
        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
