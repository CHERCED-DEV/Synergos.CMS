using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

public class AppsettingsFeatureGateTests
{
    [Fact]
    public void IsEnabled_ReturnsTrue_WhenGateConfiguredTrue()
    {
        var settings = new FeatureFlagsSettings
        {
            Gates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["alpha"] = true,
            },
        };
        var sut = new AppsettingsFeatureGate(settings);

        Assert.True(sut.IsEnabled("alpha"));
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenGateConfiguredFalse()
    {
        var settings = new FeatureFlagsSettings
        {
            Gates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["beta"] = false,
            },
        };
        var sut = new AppsettingsFeatureGate(settings);

        Assert.False(sut.IsEnabled("beta"));
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenGateUnknown()
    {
        var sut = new AppsettingsFeatureGate(new FeatureFlagsSettings());

        Assert.False(sut.IsEnabled("nonexistent"));
    }

    [Fact]
    public void IsEnabled_IsCaseInsensitive()
    {
        var settings = new FeatureFlagsSettings
        {
            Gates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["GreenBanner"] = true,
            },
        };
        var sut = new AppsettingsFeatureGate(settings);

        Assert.True(sut.IsEnabled("greenbanner"));
        Assert.True(sut.IsEnabled("GREENBANNER"));
    }

    [Fact]
    public void IsEnabled_ReturnsFalse_WhenGateNameEmpty()
    {
        var sut = new AppsettingsFeatureGate(new FeatureFlagsSettings());

        Assert.False(sut.IsEnabled(string.Empty));
    }
}
