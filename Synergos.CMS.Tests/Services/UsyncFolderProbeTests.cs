using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

public sealed class UsyncFolderProbeTests : IDisposable
{
    private readonly string _tempRoot;

    public UsyncFolderProbeTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "synergos-usync-tests-" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task CheckAsync_ReportsHealthy_WhenFolderExists()
    {
        Directory.CreateDirectory(_tempRoot);
        var probe = new UsyncFolderProbe(_tempRoot);

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Equal("usync_folder_readable", result.Name);
    }

    [Fact]
    public async Task CheckAsync_ReportsUnhealthy_WhenFolderMissing()
    {
        var probe = new UsyncFolderProbe(_tempRoot);

        var result = await probe.CheckAsync();

        Assert.False(result.IsHealthy);
        Assert.Contains("not found", result.Message);
    }

    [Fact]
    public void Constructor_ThrowsOnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => new UsyncFolderProbe(""));
    }
}
