using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMemberAuthService"/> implementado sobre
/// <see cref="IMemberManager"/> + <see cref="IMemberSignInManager"/>
/// de Umbraco.
/// </summary>
/// <remarks>
/// Vive en <c>Synergos.CMS.Web</c> porque depende de tipos
/// Umbraco.Cms.Core.Security (no se filtran a Application por ADR 0002).
/// </remarks>
public sealed class DefaultMemberAuthService : IMemberAuthService
{
    private readonly IMemberManager _memberManager;
    private readonly IMemberSignInManager _signInManager;
    private readonly ILogger<DefaultMemberAuthService> _logger;

    public DefaultMemberAuthService(
        IMemberManager memberManager,
        IMemberSignInManager signInManager,
        ILogger<DefaultMemberAuthService> logger)
    {
        _memberManager = memberManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public async Task<MemberAuthResult> RegisterAsync(
        MemberRegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return MemberAuthResult.Fail("invalid-input",
                "Email, contraseña y nombre son obligatorios.");
        }

        var existing = await _memberManager.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return MemberAuthResult.Fail("email-taken",
                "Este email ya está registrado.");
        }

        var user = MemberIdentityUser.CreateNew(
            username: request.Email,
            email: request.Email,
            memberTypeAlias: "Member",
            isApproved: true,
            name: request.DisplayName);

        var createResult = await _memberManager.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            var first = createResult.Errors.FirstOrDefault();
            var code = MapIdentityCode(first?.Code);
            _logger.LogWarning(
                "Member registration failed: email={Email} code={Code} description={Description}",
                request.Email,
                first?.Code,
                first?.Description);
            return MemberAuthResult.Fail(code, first?.Description ?? "No se pudo registrar.");
        }

        if (request.SignInImmediately)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
        }

        return MemberAuthResult.Ok();
    }

    public async Task<MemberAuthResult> LoginAsync(
        string emailOrUsername,
        string password,
        bool isPersistent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername) ||
            string.IsNullOrWhiteSpace(password))
        {
            return MemberAuthResult.Fail("invalid-credentials",
                "Email y contraseña son obligatorios.");
        }

        var result = await _signInManager.PasswordSignInAsync(
            userName: emailOrUsername,
            password: password,
            isPersistent: isPersistent,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            return MemberAuthResult.Ok();
        }
        if (result.IsLockedOut)
        {
            return MemberAuthResult.Fail("locked-out",
                "Cuenta bloqueada temporalmente por intentos fallidos.");
        }
        return MemberAuthResult.Fail("invalid-credentials",
            "Email o contraseña incorrectos.");
    }

    public Task LogoutAsync(CancellationToken cancellationToken) =>
        _signInManager.SignOutAsync();

    public async Task<LoginValidationResult> ValidateCredentialsAsync(
        string emailOrUsername,
        string password,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(emailOrUsername) || string.IsNullOrWhiteSpace(password))
        {
            return new LoginValidationResult(false, null, null, "invalid-credentials");
        }

        var user = await _memberManager.FindByEmailAsync(emailOrUsername)
            ?? await _memberManager.FindByNameAsync(emailOrUsername);
        if (user is null)
        {
            return new LoginValidationResult(false, null, null, "invalid-credentials");
        }

        if (await _memberManager.IsLockedOutAsync(user))
        {
            return new LoginValidationResult(false, null, null, "locked-out");
        }

        var passwordOk = await _memberManager.CheckPasswordAsync(user, password);
        if (!passwordOk)
        {
            // Increment failed attempts counter (Identity standard).
            await _memberManager.AccessFailedAsync(user);
            return new LoginValidationResult(false, null, null, "invalid-credentials");
        }

        // Reset failed counter post-success.
        await _memberManager.ResetAccessFailedCountAsync(user);

        return new LoginValidationResult(
            Success: true,
            Email: user.Email,
            MemberKey: user.Key,
            ErrorCode: null);
    }

    public async Task<MemberAuthResult> SignInByEmailAsync(
        string email,
        bool isPersistent,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return MemberAuthResult.Fail("invalid-credentials", "Email vacío.");
        }

        var user = await _memberManager.FindByEmailAsync(email);
        if (user is null)
        {
            return MemberAuthResult.Fail("invalid-credentials", "Member no encontrado.");
        }

        await _signInManager.SignInAsync(user, isPersistent);
        return MemberAuthResult.Ok();
    }

    public async Task<MemberAuthResult> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var user = await _memberManager.GetCurrentMemberAsync();
        if (user is null)
        {
            return MemberAuthResult.Fail("not-authenticated",
                "Sesión expirada.");
        }

        var result = await _memberManager.ChangePasswordAsync(
            user, currentPassword, newPassword);
        if (result.Succeeded)
        {
            return MemberAuthResult.Ok();
        }

        var first = result.Errors.FirstOrDefault();
        var code = MapIdentityCode(first?.Code);
        return MemberAuthResult.Fail(code,
            first?.Description ?? "No se pudo cambiar la contraseña.");
    }

    public async Task<PasswordResetRequestResult> RequestPasswordResetAsync(
        string email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new PasswordResetRequestResult(EmailExists: false, Token: null, DisplayName: null);
        }

        var user = await _memberManager.FindByEmailAsync(email);
        if (user is null)
        {
            // No leak — devolver OK aunque el email no exista para
            // evitar enumeration attacks. El caller envia email solo
            // si EmailExists=true.
            _logger.LogInformation(
                "Password reset requested for non-existent email={Email}",
                email);
            return new PasswordResetRequestResult(EmailExists: false, Token: null, DisplayName: null);
        }

        var token = await _memberManager.GeneratePasswordResetTokenAsync(user);
        return new PasswordResetRequestResult(
            EmailExists: true,
            Token: token,
            DisplayName: user.Name ?? user.UserName ?? email);
    }

    public async Task<MemberAuthResult> ConfirmPasswordResetAsync(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) ||
            string.IsNullOrWhiteSpace(token) ||
            string.IsNullOrWhiteSpace(newPassword))
        {
            return MemberAuthResult.Fail("invalid-input",
                "Email, token y nueva contraseña son obligatorios.");
        }

        var user = await _memberManager.FindByEmailAsync(email);
        if (user is null)
        {
            // Mismo razonamiento de no-leak — pero aquí sí devolvemos
            // fail porque sin user no hay reset posible. Mensaje
            // genérico para no distinguir "email no existe" de
            // "token inválido".
            return MemberAuthResult.Fail("invalid-token",
                "El link de reset no es válido o ya expiró.");
        }

        var result = await _memberManager.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Password reset succeeded: email={Email}",
                email);
            return MemberAuthResult.Ok();
        }

        var first = result.Errors.FirstOrDefault();
        var code = MapIdentityCode(first?.Code);
        if (code == "unknown" &&
            (first?.Code == "InvalidToken" || first?.Code == "InvalidUserToken"))
        {
            code = "invalid-token";
        }
        _logger.LogWarning(
            "Password reset failed: email={Email} code={Code} description={Description}",
            email,
            first?.Code,
            first?.Description);
        return MemberAuthResult.Fail(code,
            first?.Description ?? "No se pudo cambiar la contraseña.");
    }

    public async Task<EmailConfirmationRequestResult> RequestEmailConfirmationAsync(
        string email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return new EmailConfirmationRequestResult(false, false, null, null);
        }

        var user = await _memberManager.FindByEmailAsync(email);
        if (user is null)
        {
            return new EmailConfirmationRequestResult(false, false, null, null);
        }

        if (user.EmailConfirmed)
        {
            return new EmailConfirmationRequestResult(true, true, null, user.Name ?? email);
        }

        var token = await _memberManager.GenerateEmailConfirmationTokenAsync(user);
        return new EmailConfirmationRequestResult(
            MemberExists: true,
            AlreadyConfirmed: false,
            Token: token,
            DisplayName: user.Name ?? user.UserName ?? email);
    }

    public async Task<MemberAuthResult> ConfirmEmailAsync(
        string email,
        string token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return MemberAuthResult.Fail("invalid-input",
                "Email y token son obligatorios.");
        }

        var user = await _memberManager.FindByEmailAsync(email);
        if (user is null)
        {
            return MemberAuthResult.Fail("invalid-token",
                "El link de confirmación no es válido o ya expiró.");
        }

        if (user.EmailConfirmed)
        {
            // Idempotente — ya confirmado previamente, OK.
            return MemberAuthResult.Ok();
        }

        var result = await _memberManager.ConfirmEmailAsync(user, token);
        if (result.Succeeded)
        {
            _logger.LogInformation(
                "Email confirmed: email={Email}",
                email);
            return MemberAuthResult.Ok();
        }

        var first = result.Errors.FirstOrDefault();
        var code = MapIdentityCode(first?.Code);
        if (code == "unknown" &&
            (first?.Code == "InvalidToken" || first?.Code == "InvalidUserToken"))
        {
            code = "invalid-token";
        }
        return MemberAuthResult.Fail(code,
            first?.Description ?? "No se pudo confirmar el email.");
    }

    private static string MapIdentityCode(string? identityCode) =>
        identityCode switch
        {
            "DuplicateUserName" or "DuplicateEmail" => "email-taken",
            "PasswordTooShort" or "PasswordRequiresDigit" or
                "PasswordRequiresLower" or "PasswordRequiresUpper" or
                "PasswordRequiresNonAlphanumeric" => "weak-password",
            "PasswordMismatch" => "current-password-wrong",
            "InvalidToken" or "InvalidUserToken" => "invalid-token",
            _ => "unknown",
        };
}
