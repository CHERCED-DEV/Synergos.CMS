using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _emailRenderer;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IMemberAuthService authService,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        IEmailService emailService,
        IEmailTemplateRenderer emailRenderer,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _gate = gate;
        _analytics = analytics;
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _logger = logger;
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

    [HttpGet("forgot-password")]
    [AllowAnonymous]
    public IActionResult ForgotPassword([FromQuery(Name = "msg")] string? messageCode = null)
    {
        ViewData["MessageCode"] = messageCode;
        return View();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPasswordPost(
        string email,
        CancellationToken cancellationToken)
    {
        // Anti-enumeration: respondemos OK siempre, aunque el email
        // no exista. Solo enviamos si EmailExists=true.
        var resetRequest = await _authService.RequestPasswordResetAsync(email, cancellationToken);

        if (resetRequest.EmailExists && !string.IsNullOrWhiteSpace(resetRequest.Token))
        {
            var resetUrl = $"{Request.Scheme}://{Request.Host}/account/reset-password" +
                $"?email={Uri.EscapeDataString(email)}" +
                $"&token={Uri.EscapeDataString(resetRequest.Token)}";

            var bodyHtml = await _emailRenderer.RenderAsync(
                viewName: "PasswordReset",
                model: new Synergos.CMS.Web.Services.PasswordResetEmailModel(
                    DisplayName: resetRequest.DisplayName ?? email,
                    ResetUrl: resetUrl,
                    SiteName: "Synergos"),
                cancellationToken);

            await _emailService.SendAsync(new EmailMessage(
                To: email,
                Subject: "Restablece tu contraseña",
                BodyHtml: bodyHtml),
                cancellationToken);

            _analytics.Track("account.password-reset-requested", new Dictionary<string, object?>
            {
                ["email"] = email,
            });
        }
        else
        {
            // Log para auditoría pero no leak al UI.
            _logger.LogInformation(
                "Password reset requested for unknown email={Email} — no email sent (anti-enumeration)",
                email);
        }

        return RedirectToAction(nameof(ForgotPassword), new { msg = "sent" });
    }

    [HttpGet("reset-password")]
    [AllowAnonymous]
    public IActionResult ResetPassword(
        [FromQuery] string? email,
        [FromQuery] string? token,
        [FromQuery(Name = "error")] string? errorCode = null)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction(nameof(ForgotPassword),
                new { msg = "invalid-link" });
        }

        ViewData["Email"] = email;
        ViewData["Token"] = token;
        ViewData["ErrorCode"] = errorCode;
        return View();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPasswordPost(
        string email,
        string token,
        string newPassword,
        CancellationToken cancellationToken)
    {
        var result = await _authService.ConfirmPasswordResetAsync(
            email, token, newPassword, cancellationToken);

        if (!result.Success)
        {
            _analytics.Track("account.password-reset-failed", new Dictionary<string, object?>
            {
                ["errorCode"] = result.ErrorCode,
            });
            return RedirectToAction(nameof(ResetPassword), new
            {
                email,
                token,
                error = result.ErrorCode,
            });
        }

        _analytics.Track("account.password-reset-completed");
        return RedirectToAction(nameof(Login), new { msg = "password-reset-completed" });
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
