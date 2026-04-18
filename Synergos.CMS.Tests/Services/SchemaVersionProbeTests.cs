using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

public class SchemaVersionProbeTests
{
    [Fact]
    public async Task CheckAsync_ReportsHealthy_WithDefaultVersion()
    {
        var probe = new SchemaVersionProbe();

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Equal("schema_version", result.Name);
        Assert.Equal(SchemaVersionProbe.InitialVersion, result.Message);
    }

    [Fact]
    public async Task CheckAsync_ReportsExplicitVersion()
    {
        var probe = new SchemaVersionProbe("1.2.3");

        var result = await probe.CheckAsync();

        Assert.True(result.IsHealthy);
        Assert.Equal("1.2.3", result.Message);
    }

    [Fact]
    public async Task CheckAsync_FallsBackToInitialVersion_WhenBlank()
    {
        var probe = new SchemaVersionProbe("   ");

        var result = await probe.CheckAsync();

        Assert.Equal(SchemaVersionProbe.InitialVersion, result.Message);
    }
}
