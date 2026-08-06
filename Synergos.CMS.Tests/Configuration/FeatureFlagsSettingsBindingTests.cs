using Microsoft.Extensions.Configuration;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Configuration;

/// <summary>
/// Verifies <see cref="FeatureFlagsSettings"/> binds correctly from the
/// configuration section that <c>OptionsComposer</c> consumes
/// (<c>Synergos:FeatureFlags</c>).
/// </summary>
public class FeatureFlagsSettingsBindingTests
{
    [Fact]
    public void Binds_MultipleGates_FromSynergosFeatureFlagsSection()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Synergos:FeatureFlags:Gates:alpha"] = "true",
                ["Synergos:FeatureFlags:Gates:beta"] = "false",
            })
            .Build();

        var settings = new FeatureFlagsSettings();
        config.GetSection("Synergos:FeatureFlags").Bind(settings);

        Assert.True(settings.Gates.ContainsKey("alpha"));
        Assert.True(settings.Gates.ContainsKey("beta"));
        Assert.True(settings.Gates["alpha"]);
        Assert.False(settings.Gates["beta"]);
    }

    [Fact]
    public void Binds_EmptyGates_WhenSectionMissing()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var settings = new FeatureFlagsSettings();
        config.GetSection("Synergos:FeatureFlags").Bind(settings);

        Assert.Empty(settings.Gates);
    }

    [Fact]
    public void BoundGates_AreCaseInsensitive_MatchingAppsettingsFeatureGateContract()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Synergos:FeatureFlags:Gates:MixedCaseGate"] = "true",
            })
            .Build();

        var settings = new FeatureFlagsSettings();
        config.GetSection("Synergos:FeatureFlags").Bind(settings);

        // FeatureFlagsSettings.Gates is initialised with OrdinalIgnoreCase
        // comparer, and binding preserves it (binder adds to the existing
        // dictionary rather than replacing it).
        Assert.True(settings.Gates.ContainsKey("mixedcasegate"));
        Assert.True(settings.Gates.ContainsKey("MIXEDCASEGATE"));
    }
}
