using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Member self-service: login, register, profile, change password,
/// logout. ADR 0034 (Ola 64). Endpoints atributo-routed bajo
/// <c>/account</c>. POSTs delegan al <see cref="IMemberAuthService"/>
/// seam y hacen PRG (POST → Redirect → GET) preservando feedback via
/// querystring.
/// </summary>
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly IMemberAuthService _authService;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;

    public AccountController(
        IMemberAuthService authService,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics)
    {
        _authService = authService;
        _gate = gate;
        _analytics = analytics;
    }

    [HttpGet("login")]
    [AllowAnonymous]
    public IActionResult Login([FromQuery] string? returnUrl = null,
        [FromQuery(Name = "error")] string? errorCode = null)
    {
        ViewData["ReturnUrl"] = SafeReturnUrl(returnUrl);
        ViewData["ErrorCode"] = errorCode;
        return View();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> LoginPost(
        string emailOrUsername,
        string password,
        bool rememberMe,
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        var result = await _authService.LoginAsync(
            emailOrUsername, password, rememberMe, cancellationToken);

        if (!result.Success)
        {
            _analytics.Track("account.login-failed", new Dictionary<string, object?>
            {
                ["errorCode"] = result.ErrorCode,
            });
            return RedirectToAction(nameof(Login), new
            {
                returnUrl,
                error = result.ErrorCode,
            });
        }

        _analytics.Track("account.login");
        return Redirect(SafeReturnUrl(returnUrl));
    }

    [HttpGet("register")]
    [AllowAnonymous]
    public IActionResult Register([FromQuery(Name = "error")] string? errorCode = null)
    {
        ViewData["ErrorCode"] = errorCode;
        return View();
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<IActionResult> RegisterPost(
        string email,
        string password,
        string displayName,
        CancellationToken cancellationToken)
    {
        var result = await _authService.RegisterAsync(
            new MemberRegisterRequest(
                Email: email,
                Password: password,
                DisplayName: displayName,
                SignInImmediately: true),
            cancellationToken);

        if (!result.Success)
        {
            _analytics.Track("account.register-failed", new Dictionary<string, object?>
            {
                ["errorCode"] = result.ErrorCode,
            });
            return RedirectToAction(nameof(Register), new { error = result.ErrorCode });
        }

        _analytics.Track("account.registered");
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(string? returnUrl,
        CancellationToken cancellationToken)
    {
        await _authService.LogoutAsync(cancellationToken);
        _analytics.Track("account.logout");
        return Redirect(SafeReturnUrl(returnUrl));
    }

    [HttpGet("profile")]
    public IActionResult Profile([FromQuery(Name = "msg")] string? messageCode = null)
    {
        if (!_gate.IsAuthenticated)
        {
            return RedirectToAction(nameof(Login),
                new { returnUrl = "/account/profile" });
        }

        ViewData["DisplayName"] = _gate.CurrentMemberDisplayName;
        ViewData["Roles"] = _gate.CurrentMemberRoles;
        ViewData["MessageCode"] = messageCode;
        return View();
    }

    [HttpPost("profile/password")]
    public async Task<IActionResult> ChangePassword(
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken)
    {
        if (!_gate.IsAuthenticated)
        {
            return RedirectToAction(nameof(Login),
                new { returnUrl = "/account/profile" });
        }

        var result = await _authService.ChangePasswordAsync(
            currentPassword, newPassword, cancellationToken);

        return RedirectToAction(nameof(Profile),
            new { msg = result.Success ? "password-changed" : result.ErrorCode });
    }

    private static string SafeReturnUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "/";
        }
        if (Uri.TryCreate(raw, UriKind.Relative, out _))
        {
            return raw;
        }
        return "/";
    }
}
