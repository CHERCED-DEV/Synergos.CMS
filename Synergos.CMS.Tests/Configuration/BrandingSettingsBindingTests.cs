using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Configuration;

/// <summary>
/// Verifies <see cref="BrandingSettings"/> binds correctly from the
/// configuration section that <c>OptionsComposer</c> consumes
/// (<c>Synergos:Branding</c>).
/// </summary>
public class BrandingSettingsBindingTests
{
    [Fact]
    public void Binds_KeyAndDisplayName_FromSynergosBrandingSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Synergos:Branding:Key"] = "acme",
                ["Synergos:Branding:DisplayName"] = "Acme Corp",
            })
            .Build();

        var settings = new BrandingSettings();
        config.GetSection("Synergos:Branding").Bind(settings);

        Assert.Equal("acme", settings.Key);
        Assert.Equal("Acme Corp", settings.DisplayName);
    }

    [Fact]
    public void Retains_Defaults_WhenSectionMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var settings = new BrandingSettings();
        config.GetSection("Synergos:Branding").Bind(settings);

        Assert.Equal("default", settings.Key);
        Assert.Equal("Synergos", settings.DisplayName);
    }
}
