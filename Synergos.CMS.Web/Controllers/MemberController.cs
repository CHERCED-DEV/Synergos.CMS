using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Security;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Login / logout endpoints for Umbraco Members (Ola 40 runtime wiring
/// for Ola 35 schema — elementMemberLogin / elementMemberLogout).
/// </summary>
/// <remarks>
/// The renderers emit <c>&lt;form method="post"&gt;</c> pointing at the
/// <c>loginEndpoint</c> / <c>logoutEndpoint</c> props configured by the
/// editor. Typical values: <c>/member/login</c> and <c>/member/logout</c>
/// (the routes declared below). The controller uses Umbraco's
/// <see cref="IMemberSignInManager"/> to authenticate with cookie auth;
/// on success it redirects to the form's hidden <c>returnUrl</c> (or
/// home if none). On failure it redirects back with <c>?loginFailed=1</c>
/// so the design-system JS can surface a toast.
/// </remarks>
[ApiController]
[Route("member")]
public sealed class MemberController : ControllerBase
{
    private readonly IMemberSignInManager _signInManager;

    public MemberController(IMemberSignInManager signInManager) =>
        _signInManager = signInManager;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginAsync(
        [FromForm] string email,
        [FromForm] string password,
        [FromForm] string? returnUrl,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Redirect(BuildFailureRedirect(returnUrl));
        }

        var result = await _signInManager
            .PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: false)
            .ConfigureAwait(false);

        if (!result.Succeeded)
        {
            return Redirect(BuildFailureRedirect(returnUrl));
        }

        return Redirect(ResolveReturnUrl(returnUrl));
    }

    [HttpPost("logout")]
    public async Task<IActionResult> LogoutAsync(
        [FromForm] string? returnUrl,
        CancellationToken ct)
    {
        await _signInManager.SignOutAsync().ConfigureAwait(false);
        return Redirect(ResolveReturnUrl(returnUrl));
    }

    private static string ResolveReturnUrl(string? returnUrl)
    {
        // Only accept local paths — protect against open redirect.
        if (!string.IsNullOrWhiteSpace(returnUrl) &&
            Uri.TryCreate(returnUrl, UriKind.Relative, out _))
        {
            return returnUrl;
        }
        return "/";
    }

    private static string BuildFailureRedirect(string? returnUrl)
    {
        var target = ResolveReturnUrl(returnUrl);
        var separator = target.Contains('?') ? '&' : '?';
        return $"{target}{separator}loginFailed=1";
    }
}
