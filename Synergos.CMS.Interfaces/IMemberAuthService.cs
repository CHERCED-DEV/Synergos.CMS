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
/// Sin password reset por email — requiere infra SMTP que aún no está
/// en el CMS. Diferido a futura ola con <c>IPasswordResetEmailSender</c>
/// seam.
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
}

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
