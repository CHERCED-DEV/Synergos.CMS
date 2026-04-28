using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Tests.Configuration;

/// <summary>
/// Smoke tests para <see cref="DataProtectionSettings"/> defaults.
/// Cap-260 Batch C (Olas 257-258). El composer
/// <c>DataProtectionComposer</c> NO override DataProtection cuando
/// <see cref="DataProtectionSettings.KeyringPath"/> está vacío —
/// preserva el comportamiento per-instance default de ASP.NET Core.
/// </summary>
public sealed class DataProtectionSettingsTests
{
    [Fact]
    public void Defaults_KeyringPathEmpty_ComposerNoOpsThisOpt()
    {
        var s = new DataProtectionSettings();

        // El path vacío es la señal canónica para que el composer
        // NO override DataProtection — preserva default ASP.NET Core
        // (no breaking change para deployments single-instance).
        Assert.Equal(string.Empty, s.KeyringPath);
    }

    [Fact]
    public void Defaults_ApplicationName_IsSynergosCms()
    {
        var s = new DataProtectionSettings();

        // ApplicationName sirve como discriminator entre apps que
        // comparten el keyring filesystem. Default match el nombre del
        // proyecto — colision-safe en deployments uni-app.
        Assert.Equal("Synergos.CMS", s.ApplicationName);
    }

    [Fact]
    public void Defaults_KeyLifetime_Is90Days()
    {
        var s = new DataProtectionSettings();

        // 90 días = ASP.NET Core default. Bump si los secrets persistidos
        // viven más (e.g. 2FA secrets de members duran toda su vida).
        Assert.Equal(90, s.KeyLifetimeDays);
    }

    [Fact]
    public void InitOnly_AllowsOverrideViaObjectInitializer()
    {
        var s = new DataProtectionSettings
        {
            KeyringPath = "/var/syn/dp-keys",
            ApplicationName = "Synergos.CMS.Staging",
            KeyLifetimeDays = 365,
        };

        Assert.Equal("/var/syn/dp-keys", s.KeyringPath);
        Assert.Equal("Synergos.CMS.Staging", s.ApplicationName);
        Assert.Equal(365, s.KeyLifetimeDays);
    }
}
