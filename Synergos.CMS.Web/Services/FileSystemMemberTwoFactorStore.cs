using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Persistencia file-based de los secretos TOTP per Member —
/// <c>App_Data/syn-2fa/{memberKey}.json</c>. Mismo pattern que
/// FileSystemAuditTrailWriter / FileSystemCommentRepository (ADRs
/// 0067 / 0038).
/// </summary>
/// <remarks>
/// File-based en lugar de Member custom property porque:
/// 1. No requiere uSync schema change (rapid iteration).
/// 2. No accidental export del secret via uSync ExportOnSave.
/// 3. Permite encryption-at-rest sin migrar backoffice properties.
///
/// Encryption-at-rest (Olas 221-222, ADR 0084):
/// El JSON serializado se envuelve con <see cref="IDataProtector"/>
/// (purpose <c>"Synergos.MemberTwoFactor.v1"</c>). Master key bajo
/// `App_Data/Keys/` (ASP.NET Core data protection default).
/// Migración transparente: si Read encuentra plaintext legacy
/// (Olas 178-180 pre-encryption), parsea directo + re-graba
/// encrypted en el siguiente Save.
///
/// Trade-off: no scale para multi-instance LB sin shared filesystem
/// O shared keyring. Para multi-instance, configurar data protection
/// con shared key persistence (Redis / Azure KeyVault).
/// </remarks>
public sealed class FileSystemMemberTwoFactorStore
{
    private const string DirectoryName = "syn-2fa";
    private const string ProtectorPurpose = "Synergos.MemberTwoFactor.v1";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IDataProtector _protector;
    private readonly object _writeLock = new();

    public FileSystemMemberTwoFactorStore(
        IHostEnvironment hostEnvironment,
        IDataProtectionProvider dataProtectionProvider)
    {
        _hostEnvironment = hostEnvironment;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
    }

    public TwoFactorRecord? Read(Guid memberKey)
    {
        var path = ResolvePath(memberKey);
        if (!File.Exists(path)) return null;

        string json;
        try
        {
            var content = File.ReadAllText(path);
            try
            {
                // Path canónico: encrypted.
                json = _protector.Unprotect(content);
            }
            catch (CryptographicException)
            {
                // Legacy fallback: plaintext de pre-Ola 221. Acepta + se
                // re-encriptará en el próximo Save.
                json = content;
            }

            return JsonSerializer.Deserialize<TwoFactorRecord>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Save(Guid memberKey, TwoFactorRecord record)
    {
        var path = ResolvePath(memberKey);
        var json = JsonSerializer.Serialize(record, JsonOpts);
        var protectedContent = _protector.Protect(json);
        lock (_writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, protectedContent);
        }
    }

    public bool Delete(Guid memberKey)
    {
        var path = ResolvePath(memberKey);
        if (!File.Exists(path)) return false;
        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private string ResolvePath(Guid memberKey)
    {
        var fileName = memberKey.ToString("N") + ".json";
        return Path.Combine(
            _hostEnvironment.ContentRootPath,
            "App_Data",
            DirectoryName,
            fileName);
    }
}

/// <summary>
/// Persistido per-Member en file. SecretBase32 es el shared secret
/// TOTP. IsEnabled indica si completó enrollment confirmando el
/// primer código. RecoveryCodes son hashes PBKDF2 (single-use) de
/// los 8 recovery codes generados en enrollment.
/// </summary>
public sealed record TwoFactorRecord(
    string SecretBase32,
    bool IsEnabled,
    IReadOnlyList<string> RecoveryCodes,
    DateTime EnrolledUtc);
