using System.Text.Json;

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
/// 3. Permite encryption-at-rest en una ola futura sin migrar
///    backoffice properties.
///
/// Trade-off: no scale para multi-instance LB sin shared filesystem.
/// Cuando llegue ese requirement, swap por adapter sobre DB / KMS.
///
/// Olas 178.
/// </remarks>
public sealed class FileSystemMemberTwoFactorStore
{
    private const string DirectoryName = "syn-2fa";
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private readonly IHostEnvironment _hostEnvironment;
    private readonly object _writeLock = new();

    public FileSystemMemberTwoFactorStore(IHostEnvironment hostEnvironment) =>
        _hostEnvironment = hostEnvironment;

    public TwoFactorRecord? Read(Guid memberKey)
    {
        var path = ResolvePath(memberKey);
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
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
        lock (_writeLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, json);
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
/// primer código. RecoveryCodes deferido a Phase 2 (default empty).
/// </summary>
public sealed record TwoFactorRecord(
    string SecretBase32,
    bool IsEnabled,
    IReadOnlyList<string> RecoveryCodes,
    DateTime EnrolledUtc);
