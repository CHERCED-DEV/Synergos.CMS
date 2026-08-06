namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de operaciones write de autenticación de miembros: registrar,
/// iniciar sesión, cerrar sesión, cambiar contraseña. Complementa
/// <see cref="IMemberAccessGate"/> (read-only del miembro actual).
/// </summary>
/// <remarks>
/// Implementación por defecto en
/// <c>Synergos.CMS.Web.Services.DefaultMemberAuthService</c> usando
/// los managers de Umbraco Members. Si el sitio adopta Synergos.API
/// como auth provider externo, swap del binding sin tocar
/// AccountController ni templates Razor.
///
/// Password reset por email habilitado en Ola 80 vía
/// <see cref="IEmailService"/> + <c>IMemberManager.GeneratePasswordResetTokenAsync</c>.
/// </remarks>
public interface IMemberAuthService
{
    /// <summary>
    /// Crea un miembro nuevo y opcionalmente firma sesión inmediata.
    /// </summary>
    Task<MemberAuthResult> RegisterAsync(
        MemberRegisterRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifica credenciales y firma sesión del miembro.
    /// </summary>
    Task<MemberAuthResult> LoginAsync(
        string emailOrUsername,
        string password,
        bool isPersistent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Verifica credenciales SIN firmar sesión — útil para flows que
    /// necesitan un challenge intermedio (e.g. 2FA TOTP). Si retorna
    /// <see cref="LoginValidationResult.Success"/> true, el caller
    /// puede invocar <see cref="SignInByEmailAsync"/> tras el challenge.
    /// (Ola 201, ADR 0082)
    /// </summary>
    Task<LoginValidationResult> ValidateCredentialsAsync(
        string emailOrUsername,
        string password,
        CancellationToken cancellationToken);

    /// <summary>
    /// Firma sesión del miembro identificado por email — usado por
    /// flows post-challenge donde la password ya fue validada.
    /// (Ola 201)
    /// </summary>
    Task<MemberAuthResult> SignInByEmailAsync(
        string email,
        bool isPersistent,
        CancellationToken cancellationToken);

    /// <summary>
    /// Cierra la sesión del miembro actual.
    /// </summary>
    Task LogoutAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Cambia la contraseña del miembro autenticado actual. Requiere
    /// la contraseña actual para verificación.
    /// </summary>
    Task<MemberAuthResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Genera un token de reset de contraseña para el email indicado y
    /// lo devuelve para que el caller construya el link de email.
    /// Si el email no existe, devuelve OK igual (no leak — defensa
    /// contra enumeration attacks). El email envío lo hace el caller
    /// vía <see cref="IEmailService"/>.
    /// </summary>
    Task<PasswordResetRequestResult> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Valida el token + cambia la contraseña del miembro identificado
    /// por <paramref name="email"/>. Token expira según política de
    /// Umbraco (default 1h).
    /// </summary>
    Task<MemberAuthResult> ConfirmPasswordResetAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken);

    /// <summary>
    /// Genera un token de confirmación de email para el miembro recién
    /// registrado. Caller compone link absoluto + envía email.
    /// </summary>
    Task<EmailConfirmationRequestResult> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken);

    /// <summary>
    /// Valida el token + marca el email del miembro como confirmado.
    /// </summary>
    Task<MemberAuthResult> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultado del request de confirmación de email. Si el miembro no
/// existe o ya está confirmado, <see cref="Token"/> es null.
/// </summary>
public sealed record EmailConfirmationRequestResult(
    bool MemberExists,
    bool AlreadyConfirmed,
    string? Token,
    string? DisplayName);

/// <summary>
/// Resultado de un request de reset. Cuando <see cref="EmailExists"/>
/// false, <see cref="Token"/> es null — el caller debe responder OK
/// igualmente (no leak).
/// </summary>
/// <param name="EmailExists">true si el email corresponde a un member.</param>
/// <param name="Token">Token a embeber en el link de email. Null si email no existe.</param>
/// <param name="DisplayName">Nombre del member (para personalizar email). Null si no existe.</param>
public sealed record PasswordResetRequestResult(
    bool EmailExists,
    string? Token,
    string? DisplayName);

/// <summary>
/// Resultado de validación de credenciales sin firmar sesión.
/// (Ola 201, ADR 0082)
/// </summary>
/// <param name="Success">True si email + password matchean Member válido.</param>
/// <param name="Email">Email canónico del Member (lowercase) cuando Success=true.</param>
/// <param name="MemberKey">Member.Key (Guid) cuando Success=true.</param>
/// <param name="ErrorCode">slug del error: "invalid-credentials" /
///   "locked-out". Null cuando Success=true.</param>
public sealed record LoginValidationResult(
    bool Success,
    string? Email,
    Guid? MemberKey,
    string? ErrorCode);

/// <summary>
/// Datos del nuevo miembro a registrar.
/// </summary>
/// <param name="Email">Email único, será también el username.</param>
/// <param name="Password">Contraseña en texto plano (validada por
///   policy de Umbraco — longitud mínima, complejidad).</param>
/// <param name="DisplayName">Nombre visible del miembro.</param>
/// <param name="SignInImmediately">Si true, firma sesión post-creación
///   sin requerir verificación de email.</param>
public sealed record MemberRegisterRequest(
    string Email,
    string Password,
    string DisplayName,
    bool SignInImmediately = true);

/// <summary>
/// Resultado de una operación auth. <see cref="Success"/> false trae
/// <see cref="ErrorCode"/> machine-readable y <see cref="ErrorMessage"/>
/// human-readable (puede ser localizable via dictionary keys).
/// </summary>
/// <param name="Success">true cuando la operación se completó.</param>
/// <param name="ErrorCode">slug del error: "invalid-credentials",
///   "email-taken", "weak-password", "current-password-wrong",
///   "not-authenticated", "unknown".</param>
/// <param name="ErrorMessage">Descripción human-readable del error.</param>
public sealed record MemberAuthResult(
    bool Success,
    string? ErrorCode = null,
    string? ErrorMessage = null)
{
    public static MemberAuthResult Ok() => new(Success: true);

    public static MemberAuthResult Fail(string errorCode, string? message = null) =>
        new(Success: false, ErrorCode: errorCode, ErrorMessage: message);
}
