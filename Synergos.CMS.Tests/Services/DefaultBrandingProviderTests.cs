using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Tests.Services;

public class DefaultBrandingProviderTests
{
    [Fact]
    public void GetCurrent_ReturnsIdentityFromSettings()
    {
        var settings = new BrandingSettings { Key = "acme", DisplayName = "Acme Corp" };
        var sut = new DefaultBrandingProvider(settings);

        var identity = sut.GetCurrent();

        Assert.Equal("acme", identity.Key);
        Assert.Equal("Acme Corp", identity.DisplayName);
    }

    [Fact]
    public void GetCurrent_UsesSettingsDefaults_WhenNotOverridden()
    {
        var sut = new DefaultBrandingProvider(new BrandingSettings());

        var identity = sut.GetCurrent();

        Assert.Equal("default", identity.Key);
        Assert.Equal("Synergos", identity.DisplayName);
    }

    [Fact]
    public void GetCurrent_IsIdempotent_AcrossCalls()
    {
        var sut = new DefaultBrandingProvider(new BrandingSettings { Key = "x", DisplayName = "X" });

        Assert.Equal(sut.GetCurrent(), sut.GetCurrent());
    }
}
