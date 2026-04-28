using Microsoft.AspNetCore.DataProtection;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Composing;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Configura el ASP.NET Core Data Protection master keyring para
/// soportar deployments multi-instance. Cap-260 Batch C (Olas 257-258).
/// </summary>
/// <remarks>
/// Reads <see cref="DataProtectionSettings"/> at compose time (boot).
/// Si <c>KeyringPath</c> está vacío, no override — ASP.NET Core
/// preserva el default. Si poblado, persiste keys al filesystem
/// path indicado con <c>ApplicationName</c> discriminador.
///
/// El composer corre DESPUÉS de <c>OptionsComposer</c> para garantizar
/// que el binding ya está registrado en DI cuando lo consultamos.
/// Lectura directa del IConfiguration porque
/// <c>AddDataProtection</c> debe correr durante boot — no podemos
/// resolver <c>IOptions&lt;T&gt;</c> antes de que el provider exista.
///
/// Para multi-instance scale futuro, alternativas a FileSystem:
/// - <c>PersistKeysToStackExchangeRedis(...)</c> — paquete extra.
/// - <c>PersistKeysToAzureBlobStorage(...)</c> — paquete extra +
///   identity. Documentado como next-step en ADR 0087.
/// </remarks>
[ComposeAfter(typeof(OptionsComposer))]
public sealed class DataProtectionComposer : IComposer
{
    public void Compose(IUmbracoBuilder builder)
    {
        var section = builder.Config.GetSection("Synergos:DataProtection");
        var settings = new DataProtectionSettings
        {
            KeyringPath = section[nameof(DataProtectionSettings.KeyringPath)] ?? string.Empty,
            ApplicationName = section[nameof(DataProtectionSettings.ApplicationName)] ?? "Synergos.CMS",
            KeyLifetimeDays = int.TryParse(
                section[nameof(DataProtectionSettings.KeyLifetimeDays)], out var days) ? days : 90,
        };

        if (string.IsNullOrWhiteSpace(settings.KeyringPath))
        {
            // No override — preserva default ASP.NET Core (per-instance).
            return;
        }

        // Path inválido (no existe + no podemos crear): el operador
        // configuró algo roto. Mejor crear el dir si no existe — más
        // tolerante para containers donde el volumen monta vacío.
        Directory.CreateDirectory(settings.KeyringPath);

        builder.Services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(settings.KeyringPath))
            .SetApplicationName(settings.ApplicationName)
            .SetDefaultKeyLifetime(TimeSpan.FromDays(Math.Max(1, settings.KeyLifetimeDays)));
    }
}
