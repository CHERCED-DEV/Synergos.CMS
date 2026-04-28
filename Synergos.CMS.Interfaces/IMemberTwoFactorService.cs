namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam para Member 2FA opt-in vía TOTP (Time-based One-Time Password,
/// RFC 6238 — el algoritmo que Google Authenticator / Authy / 1Password
/// implementan). Olas 177-180.
/// </summary>
/// <remarks>
/// Diseño:
/// - <b>Opt-in</b>: Members eligen activar 2FA. No es mandatorio para
///   Members regulares; admin/moderator pueden ser forzados via
///   <c>FeatureFlagsSettings.RequireTwoFactorForRoles</c> (deferred).
/// - <b>Stateful en Member custom property</b>: el secreto TOTP se
///   persiste en una propiedad <c>twoFactorSecret</c> del DocType
///   Member (string, encrypted at rest). El flag <c>twoFactorEnabled</c>
///   indica si ya completó el enrollment.
/// - <b>Recovery codes</b>: 8 códigos de 8 chars generados en
///   enrollment. Persisted en <c>twoFactorRecoveryCodes</c> hashed.
///   Cada uso quema el código.
/// - <b>Login flow</b>: tras password OK, si <c>twoFactorEnabled</c>,
///   redirect a `/account/2fa-challenge`. Si TOTP correcto o recovery
///   code válido, completa el sign-in.
///
/// Tipo de errores:
/// - <c>EnrollmentResult</c> permite distinguir "código incorrecto"
///   vs "secreto inválido" vs "ya enrolled".
/// - <c>VerificationResult</c> permite distinguir "TOTP wrong" vs
///   "recovery used" vs "no enrolled".
///
/// Implementación pendiente — esta interfaz fija el shape, las olas
/// posteriores agregan el adapter (Otp.NET probable).
/// </remarks>
public interface IMemberTwoFactorService
{
    /// <summary>
    /// Genera un secreto TOTP nuevo + URI de provisioning (otpauth://)
    /// para que el Member lo escanee con su app TOTP. NO persiste el
    /// secreto aún — el Member debe confirmar con el primer código
    /// vía <see cref="ConfirmEnrollmentAsync"/>.
    /// </summary>
    Task<TwoFactorEnrollmentChallenge> StartEnrollmentAsync(
        Guid memberKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// Confirma el enrollment validando el primer código TOTP. Si OK,
    /// persiste el secreto + genera 8 recovery codes. Idempotent — si
    /// ya está enrolled retorna AlreadyEnrolled sin sobreescribir.
    /// </summary>
    Task<EnrollmentResult> ConfirmEnrollmentAsync(
        Guid memberKey,
        string secretBase32,
        string firstCode,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifica un código TOTP (6 digits) o recovery code (8 chars)
    /// contra el secret persistido del Member. Recovery codes son
    /// single-use — si valid, se quema antes de retornar.
    /// </summary>
    Task<VerificationResult> VerifyAsync(
        Guid memberKey,
        string codeOrRecovery,
        CancellationToken cancellationToken);

    /// <summary>
    /// Quita el 2FA del Member. Requerido para flujos de recovery
    /// admin (Member perdió el dispositivo + recovery codes; admin
    /// resetea via /admin/members/{key}/2fa-reset). No requiere
    /// confirmación del Member — el admin trail registra la acción.
    /// </summary>
    Task<bool> DisableAsync(
        Guid memberKey,
        CancellationToken cancellationToken);

    /// <summary>
    /// True si el Member tiene 2FA enrolled + active. Usado por el
    /// login flow para decidir si pedir el segundo factor.
    /// </summary>
    Task<bool> IsEnabledAsync(
        Guid memberKey,
        CancellationToken cancellationToken);
}

/// <summary>
/// Datos para que el Member complete el enrollment scanning el QR o
/// pegando el secret base32 en su app TOTP. El secreto NO está
/// persistido aún — solo se persiste tras
/// <see cref="IMemberTwoFactorService.ConfirmEnrollmentAsync"/>.
/// </summary>
/// <param name="SecretBase32">Secreto en base32 (uppercase, sin
///   padding). Lo que la app TOTP almacena.</param>
/// <param name="ProvisioningUri">URI <c>otpauth://totp/...</c> que el
///   Member escanea como QR. Incluye issuer + accountName + secret.</param>
public sealed record TwoFactorEnrollmentChallenge(
    string SecretBase32,
    string ProvisioningUri);

/// <summary>
/// Resultado de <see cref="IMemberTwoFactorService.ConfirmEnrollmentAsync"/>.
/// </summary>
public enum EnrollmentResult
{
    /// <summary>Confirm OK; recovery codes returned.</summary>
    Confirmed,

    /// <summary>El primer código TOTP no matcheó el secreto.</summary>
    InvalidCode,

    /// <summary>El Member ya estaba enrolled — no-op.</summary>
    AlreadyEnrolled,

    /// <summary>Member no existe.</summary>
    MemberNotFound,
}

/// <summary>
/// Resultado de <see cref="IMemberTwoFactorService.VerifyAsync"/>.
/// </summary>
public enum VerificationResult
{
    /// <summary>Código TOTP válido.</summary>
    TotpOk,

    /// <summary>Recovery code válido y consumido.</summary>
    RecoveryConsumed,

    /// <summary>Código incorrecto.</summary>
    Invalid,

    /// <summary>Member no tiene 2FA enabled.</summary>
    NotEnabled,
}
