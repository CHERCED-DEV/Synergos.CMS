using System.Security.Cryptography;
using OtpNet;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMemberTwoFactorService"/> impl con Otp.NET
/// (TOTP RFC 6238) + persistencia file-based via
/// <see cref="FileSystemMemberTwoFactorStore"/>.
/// </summary>
/// <remarks>
/// <b>Phase 1 scope</b> (Olas 178-180):
/// - StartEnrollmentAsync genera secret + provisioning URI.
/// - ConfirmEnrollmentAsync valida primer código + persiste enabled=true.
/// - VerifyAsync valida códigos TOTP runtime.
/// - DisableAsync borra el record (admin reset path).
/// - IsEnabledAsync consulta estado.
///
/// <b>Phase 2 deferred</b>:
/// - Recovery codes (8 single-use codes generados en enrollment,
///   hashed with PBKDF2 al persistir, recovery code consume si valid).
/// - Encryption-at-rest del secret via IDataProtectionProvider.
/// - Member self-service enrollment view (/account/2fa-setup).
/// - Login flow extension (post-password challenge).
/// </remarks>
public sealed class UmbracoMemberTwoFactorService : IMemberTwoFactorService
{
    private const int SecretBytes = 20;          // 160-bit TOTP standard
    private const string Issuer = "Synergos";
    private const int VerificationStepWindow = 1; // ±1 step (~30s) drift tolerance

    private readonly FileSystemMemberTwoFactorStore _store;
    private readonly IMemberService _memberService;

    public UmbracoMemberTwoFactorService(
        FileSystemMemberTwoFactorStore store,
        IMemberService memberService)
    {
        _store = store;
        _memberService = memberService;
    }

    public Task<TwoFactorEnrollmentChallenge> StartEnrollmentAsync(
        Guid memberKey,
        CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        var account = member?.Email ?? memberKey.ToString("N");

        var secretBytes = new byte[SecretBytes];
        RandomNumberGenerator.Fill(secretBytes);
        var secretBase32 = Base32Encoding.ToString(secretBytes);

        var provisioningUri =
            $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(account)}" +
            $"?secret={secretBase32}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

        return Task.FromResult(new TwoFactorEnrollmentChallenge(secretBase32, provisioningUri));
    }

    public Task<EnrollmentResult> ConfirmEnrollmentAsync(
        Guid memberKey,
        string secretBase32,
        string firstCode,
        CancellationToken cancellationToken)
    {
        if (_memberService.GetByKey(memberKey) is null)
        {
            return Task.FromResult(EnrollmentResult.MemberNotFound);
        }

        var existing = _store.Read(memberKey);
        if (existing is { IsEnabled: true })
        {
            return Task.FromResult(EnrollmentResult.AlreadyEnrolled);
        }

        byte[] secretBytes;
        try
        {
            secretBytes = Base32Encoding.ToBytes(secretBase32);
        }
        catch (Exception)
        {
            return Task.FromResult(EnrollmentResult.InvalidCode);
        }

        var totp = new Totp(secretBytes);
        var verified = totp.VerifyTotp(firstCode, out _, new VerificationWindow(VerificationStepWindow, VerificationStepWindow));
        if (!verified)
        {
            return Task.FromResult(EnrollmentResult.InvalidCode);
        }

        // Phase 2 — generar 8 recovery codes single-use, persistir
        // hashed con PBKDF2.
        var recoveryHashes = GenerateAndHashRecoveryCodes(out var plaintextCodes);

        _store.Save(memberKey, new TwoFactorRecord(
            SecretBase32: secretBase32,
            IsEnabled: true,
            RecoveryCodes: recoveryHashes,
            EnrolledUtc: DateTime.UtcNow));

        // Plaintext codes shown to the member ONCE — caller los obtiene
        // para mostrar en la confirmación. Almacenados en TempData del
        // AdminController flow.
        _lastEnrollmentRecoveryCodes[memberKey] = plaintextCodes;
        return Task.FromResult(EnrollmentResult.Confirmed);
    }

    /// <summary>
    /// Devuelve los recovery codes plaintext generados en el enrollment
    /// más reciente. Single-use — al leerlos se borran del cache. El
    /// caller debe mostrarlos al member exactamente una vez.
    /// </summary>
    public IReadOnlyList<string>? ConsumeLastEnrollmentRecoveryCodes(Guid memberKey)
    {
        if (_lastEnrollmentRecoveryCodes.TryRemove(memberKey, out var codes))
        {
            return codes;
        }
        return null;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, IReadOnlyList<string>> _lastEnrollmentRecoveryCodes = new();

    private const int RecoveryCodeCount = 8;
    private const int RecoveryCodeChars = 8;
    private const int Pbkdf2Iterations = 100_000;
    private const int Pbkdf2HashBytes = 32;
    private const int Pbkdf2SaltBytes = 16;
    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static (IReadOnlyList<string> hashes, IReadOnlyList<string> plaintext) GenerateRecoveryCodesAndHashes()
    {
        var plaintext = new List<string>(RecoveryCodeCount);
        var hashes = new List<string>(RecoveryCodeCount);
        var charBuf = new char[RecoveryCodeChars];

        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            for (var c = 0; c < RecoveryCodeChars; c++)
            {
                charBuf[c] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];
            }
            var code = new string(charBuf);
            plaintext.Add(code);
            hashes.Add(HashRecoveryCode(code));
        }

        return (hashes, plaintext);
    }

    private static string HashRecoveryCode(string code)
    {
        var salt = new byte[Pbkdf2SaltBytes];
        RandomNumberGenerator.Fill(salt);
        using var pbkdf2 = new Rfc2898DeriveBytes(code, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
        var hash = pbkdf2.GetBytes(Pbkdf2HashBytes);
        return Convert.ToBase64String(salt) + ":" + Convert.ToBase64String(hash);
    }

    private static bool VerifyRecoveryCode(string candidate, string saltColonHash)
    {
        var sep = saltColonHash.IndexOf(':');
        if (sep < 0) return false;
        try
        {
            var salt = Convert.FromBase64String(saltColonHash[..sep]);
            var expected = Convert.FromBase64String(saltColonHash[(sep + 1)..]);
            using var pbkdf2 = new Rfc2898DeriveBytes(candidate, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256);
            var actual = pbkdf2.GetBytes(Pbkdf2HashBytes);
            return CryptographicOperations.FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> GenerateAndHashRecoveryCodes(out IReadOnlyList<string> plaintextCodes)
    {
        var (hashes, plaintext) = GenerateRecoveryCodesAndHashes();
        plaintextCodes = plaintext;
        return hashes;
    }

    public Task<VerificationResult> VerifyAsync(
        Guid memberKey,
        string codeOrRecovery,
        CancellationToken cancellationToken)
    {
        var record = _store.Read(memberKey);
        if (record is not { IsEnabled: true })
        {
            return Task.FromResult(VerificationResult.NotEnabled);
        }

        byte[] secretBytes;
        try
        {
            secretBytes = Base32Encoding.ToBytes(record.SecretBase32);
        }
        catch (Exception)
        {
            return Task.FromResult(VerificationResult.Invalid);
        }

        var totp = new Totp(secretBytes);
        var verified = totp.VerifyTotp(codeOrRecovery, out _, new VerificationWindow(VerificationStepWindow, VerificationStepWindow));
        if (verified)
        {
            return Task.FromResult(VerificationResult.TotpOk);
        }

        // Phase 2 — recovery code branch. Iterar hashes; si match,
        // remove + persist updated record.
        if (record.RecoveryCodes.Count > 0)
        {
            var normalized = (codeOrRecovery ?? string.Empty).Trim().ToUpperInvariant();
            for (var i = 0; i < record.RecoveryCodes.Count; i++)
            {
                if (VerifyRecoveryCode(normalized, record.RecoveryCodes[i]))
                {
                    var remaining = record.RecoveryCodes.ToList();
                    remaining.RemoveAt(i);
                    _store.Save(memberKey, record with { RecoveryCodes = remaining });
                    return Task.FromResult(VerificationResult.RecoveryConsumed);
                }
            }
        }

        return Task.FromResult(VerificationResult.Invalid);
    }

    public Task<bool> DisableAsync(
        Guid memberKey,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(_store.Delete(memberKey));
    }

    public Task<bool> IsEnabledAsync(
        Guid memberKey,
        CancellationToken cancellationToken)
    {
        var record = _store.Read(memberKey);
        return Task.FromResult(record is { IsEnabled: true });
    }
}
