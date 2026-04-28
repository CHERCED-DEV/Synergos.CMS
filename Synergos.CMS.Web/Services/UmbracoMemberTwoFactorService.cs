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

        _store.Save(memberKey, new TwoFactorRecord(
            SecretBase32: secretBase32,
            IsEnabled: true,
            RecoveryCodes: Array.Empty<string>(),  // Phase 2
            EnrolledUtc: DateTime.UtcNow));

        return Task.FromResult(EnrollmentResult.Confirmed);
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

        // Phase 2 — recovery code branch (consumes single-use). Por ahora
        // no implementado.
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
