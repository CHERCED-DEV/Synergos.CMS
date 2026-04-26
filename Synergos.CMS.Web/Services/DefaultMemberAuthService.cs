using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
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

    private static string MapIdentityCode(string? identityCode) =>
        identityCode switch
        {
            "DuplicateUserName" or "DuplicateEmail" => "email-taken",
            "PasswordTooShort" or "PasswordRequiresDigit" or
                "PasswordRequiresLower" or "PasswordRequiresUpper" or
                "PasswordRequiresNonAlphanumeric" => "weak-password",
            "PasswordMismatch" => "current-password-wrong",
            _ => "unknown",
        };
}
