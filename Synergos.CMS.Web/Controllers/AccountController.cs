using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

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
    private const string TwoFactorPendingCachePrefix = "syn:2fa-pending:";
    private static readonly TimeSpan TwoFactorPendingTtl = TimeSpan.FromMinutes(5);

    private readonly IMemberAuthService _authService;
    private readonly IMemberAccessGate _gate;
    private readonly IAnalyticsTracker _analytics;
    private readonly IEmailService _emailService;
    private readonly IEmailTemplateRenderer _emailRenderer;
    private readonly IOptions<MembersSettings> _membersSettings;
    private readonly IBrandingProvider _branding;
    private readonly IMemberTwoFactorService _twoFactor;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        IMemberAuthService authService,
        IMemberAccessGate gate,
        IAnalyticsTracker analytics,
        IEmailService emailService,
        IEmailTemplateRenderer emailRenderer,
        IOptions<MembersSettings> membersSettings,
        IBrandingProvider branding,
        IMemberTwoFactorService twoFactor,
        IMemoryCache cache,
        ILogger<AccountController> logger)
    {
        _authService = authService;
        _gate = gate;
        _analytics = analytics;
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _membersSettings = membersSettings;
        _branding = branding;
        _twoFactor = twoFactor;
        _cache = cache;
        _logger = logger;
    }

    private sealed record PendingTwoFactorLogin(
        string Email,
        Guid MemberKey,
        bool IsPersistent,
        string? ReturnUrl,
        DateTime CreatedAtUtc);

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
        // Olas 201-204 — 2FA-aware login flow:
        // 1. Validate password sin sign-in.
        // 2. Si IsEnabled 2FA → cache challenge + redirect /account/2fa-challenge.
        // 3. Sino → SignInByEmailAsync directo.
        var validation = await _authService.ValidateCredentialsAsync(
            emailOrUsername, password, cancellationToken);

        if (!validation.Success)
        {
            _analytics.Track("account.login-failed", new Dictionary<string, object?>
            {
                ["errorCode"] = validation.ErrorCode,
            });
            return RedirectToAction(nameof(Login), new
            {
                returnUrl,
                error = validation.ErrorCode,
            });
        }

        var twoFactorEnabled = validation.MemberKey.HasValue
            && await _twoFactor.IsEnabledAsync(validation.MemberKey.Value, cancellationToken);

        if (twoFactorEnabled && validation.Email is not null && validation.MemberKey.HasValue)
        {
            // Cache pending challenge token. Expires en 5 min.
            var token = Guid.NewGuid().ToString("N");
            _cache.Set(
                TwoFactorPendingCachePrefix + token,
                new PendingTwoFactorLogin(
                    Email: validation.Email,
                    MemberKey: validation.MemberKey.Value,
                    IsPersistent: rememberMe,
                    ReturnUrl: returnUrl,
                    CreatedAtUtc: DateTime.UtcNow),
                TwoFactorPendingTtl);

            _analytics.Track("account.login-2fa-required", new Dictionary<string, object?>
            {
                ["memberKey"] = validation.MemberKey.Value.ToString("N"),
            });

            return RedirectToAction(nameof(TwoFactorChallenge), new { token });
        }

        var signInResult = await _authService.SignInByEmailAsync(
            validation.Email!, rememberMe, cancellationToken);
        if (!signInResult.Success)
        {
            return RedirectToAction(nameof(Login), new
            {
                returnUrl,
                error = signInResult.ErrorCode,
            });
        }

        _analytics.Track("account.login");
        return Redirect(SafeReturnUrl(returnUrl));
    }

    /// <summary>
    /// GET /account/2fa-challenge?token=X — challenge view post-password.
    /// El token vive 5min en IMemoryCache. Si expira, redirect a /account/login.
    /// (Olas 201-204)
    /// </summary>
    [HttpGet("2fa-challenge")]
    [AllowAnonymous]
    public IActionResult TwoFactorChallenge(
        [FromQuery] string token,
        [FromQuery(Name = "error")] string? errorCode = null)
    {
        if (string.IsNullOrWhiteSpace(token) ||
            _cache.Get<PendingTwoFactorLogin>(TwoFactorPendingCachePrefix + token) is null)
        {
            return RedirectToAction(nameof(Login));
        }

        ViewData["Token"] = token;
        ViewData["ErrorCode"] = errorCode;
        return View();
    }

    /// <summary>
    /// POST /account/2fa-challenge — verifica TOTP/recovery + completes
    /// sign-in. (Olas 201-204)
    /// </summary>
    [HttpPost("2fa-challenge")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactorChallengePost(
        [FromForm] string token,
        [FromForm] string code,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return RedirectToAction(nameof(Login));
        }

        var pending = _cache.Get<PendingTwoFactorLogin>(TwoFactorPendingCachePrefix + token);
        if (pending is null)
        {
            return RedirectToAction(nameof(Login),
                new { error = "2fa-token-expired" });
        }

        var verifyResult = await _twoFactor.VerifyAsync(
            pending.MemberKey,
            (code ?? string.Empty).Trim(),
            cancellationToken);

        if (verifyResult == VerificationResult.Invalid ||
            verifyResult == VerificationResult.NotEnabled)
        {
            _analytics.Track("account.login-2fa-failed", new Dictionary<string, object?>
            {
                ["memberKey"] = pending.MemberKey.ToString("N"),
                ["result"] = verifyResult.ToString(),
            });
            return RedirectToAction(nameof(TwoFactorChallenge),
                new { token, error = "invalid-code" });
        }

        // Verified OK — consume token + sign-in.
        _cache.Remove(TwoFactorPendingCachePrefix + token);

        var signInResult = await _authService.SignInByEmailAsync(
            pending.Email, pending.IsPersistent, cancellationToken);
        if (!signInResult.Success)
        {
            return RedirectToAction(nameof(Login),
                new { error = signInResult.ErrorCode });
        }

        _analytics.Track("account.login-2fa-success", new Dictionary<string, object?>
        {
            ["memberKey"] = pending.MemberKey.ToString("N"),
            ["method"] = verifyResult == VerificationResult.RecoveryConsumed ? "recovery" : "totp",
        });

        return Redirect(SafeReturnUrl(pending.ReturnUrl));
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
        var requireConfirmation = _membersSettings.Value.RequireEmailConfirmation;

        var result = await _authService.RegisterAsync(
            new MemberRegisterRequest(
                Email: email,
                Password: password,
                DisplayName: displayName,
                // Si requiere confirmación, NO sign in inmediato — visitante
                // verifica via email primero y eso firma sesión.
                SignInImmediately: !requireConfirmation),
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

        if (requireConfirmation)
        {
            await SendEmailConfirmationLinkAsync(email, cancellationToken);
            return RedirectToAction(nameof(Registered), new { email });
        }

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

            var siteName = ResolveSiteName();
            var bodyHtml = await _emailRenderer.RenderAsync(
                viewName: "PasswordReset",
                model: new Synergos.CMS.Web.Services.PasswordResetEmailModel(
                    DisplayName: resetRequest.DisplayName ?? email,
                    ResetUrl: resetUrl,
                    SiteName: siteName),
                cancellationToken);

            await _emailService.SendAsync(new EmailMessage(
                To: email,
                Subject: $"{siteName} · Restablece tu contraseña",
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

    /// <summary>
    /// Resuelve el nombre del sitio para el header del email.
    /// Ola 85: usa IBrandingProvider.GetCurrent() para que en deploys
    /// multi-brand cada email salga con el branding del request actual.
    /// Fallback "Synergos" si no hay brand activo.
    /// </summary>
    private string ResolveSiteName()
    {
        var brand = _branding.GetCurrent();
        return string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
    }

    [HttpGet("resend-confirmation")]
    [AllowAnonymous]
    public IActionResult ResendConfirmation([FromQuery(Name = "msg")] string? messageCode = null)
    {
        ViewData["MessageCode"] = messageCode;
        return View();
    }

    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmationPost(
        string email,
        CancellationToken cancellationToken)
    {
        // Anti-enumeration + idempotente: respondemos OK siempre, aunque
        // el email no exista o ya esté confirmado. Solo enviamos email
        // si MemberExists && !AlreadyConfirmed.
        await SendEmailConfirmationLinkAsync(email, cancellationToken);

        _analytics.Track("account.resend-confirmation-requested", new Dictionary<string, object?>
        {
            ["email"] = email,
        });

        return RedirectToAction(nameof(ResendConfirmation), new { msg = "sent" });
    }

    [HttpGet("registered")]
    [AllowAnonymous]
    public IActionResult Registered([FromQuery] string? email = null)
    {
        ViewData["Email"] = email;
        return View();
    }

    [HttpGet("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(
        [FromQuery] string? email,
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
        {
            ViewData["ErrorCode"] = "invalid-link";
            return View();
        }

        var result = await _authService.ConfirmEmailAsync(email, token, cancellationToken);
        if (!result.Success)
        {
            _analytics.Track("account.email-confirm-failed", new Dictionary<string, object?>
            {
                ["errorCode"] = result.ErrorCode,
            });
            ViewData["ErrorCode"] = result.ErrorCode;
            return View();
        }

        _analytics.Track("account.email-confirmed");
        return RedirectToAction(nameof(Login),
            new { msg = "email-confirmed" });
    }

    /// <summary>
    /// GET /account/2fa-setup — Member self-service enrollment de TOTP.
    /// Si ya está enabled, muestra status. Sino genera challenge fresco
    /// y muestra secret + URI para scan/paste. Olas 197-198.
    /// </summary>
    [HttpGet("2fa-setup")]
    public async Task<IActionResult> TwoFactorSetup(CancellationToken cancellationToken)
    {
        var memberKey = _gate.CurrentMemberKey;
        if (memberKey is null) return RedirectToAction(nameof(Login));

        var enabled = await _twoFactor.IsEnabledAsync(memberKey.Value, cancellationToken);
        ViewData["IsEnabled"] = enabled;

        if (!enabled)
        {
            var challenge = await _twoFactor.StartEnrollmentAsync(memberKey.Value, cancellationToken);
            ViewData["SecretBase32"] = challenge.SecretBase32;
            ViewData["ProvisioningUri"] = challenge.ProvisioningUri;
        }
        return View();
    }

    /// <summary>
    /// POST /account/2fa-setup — Confirma enrollment con primer código
    /// TOTP. En éxito muestra los recovery codes UNA vez al member.
    /// Olas 197-198.
    /// </summary>
    [HttpPost("2fa-setup")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TwoFactorSetupConfirm(
        [FromForm] string secret,
        [FromForm] string code,
        CancellationToken cancellationToken)
    {
        var memberKey = _gate.CurrentMemberKey;
        if (memberKey is null) return RedirectToAction(nameof(Login));

        var result = await _twoFactor.ConfirmEnrollmentAsync(
            memberKey.Value, secret, code ?? string.Empty, cancellationToken);

        ViewData["EnrollmentResult"] = result.ToString();

        if (result == EnrollmentResult.Confirmed && _twoFactor is UmbracoMemberTwoFactorService impl)
        {
            var recoveryCodes = impl.ConsumeLastEnrollmentRecoveryCodes(memberKey.Value);
            ViewData["RecoveryCodes"] = recoveryCodes;
            _analytics.Track("account.2fa-enrolled", new Dictionary<string, object?>
            {
                ["memberKey"] = memberKey.Value.ToString("N"),
            });
            return View("TwoFactorSetupConfirmed");
        }

        if (result == EnrollmentResult.InvalidCode)
        {
            // Re-render setup with error message + same secret (so QR
            // doesn't change between attempts).
            ViewData["IsEnabled"] = false;
            ViewData["SecretBase32"] = secret;
            ViewData["ProvisioningUri"] =
                $"otpauth://totp/Synergos:{Uri.EscapeDataString(_gate.CurrentMemberEmail ?? memberKey.Value.ToString("N"))}" +
                $"?secret={secret}&issuer=Synergos&algorithm=SHA1&digits=6&period=30";
            ViewData["ErrorCode"] = "invalid-code";
            return View(nameof(TwoFactorSetup));
        }

        return RedirectToAction(nameof(TwoFactorSetup));
    }

    private async Task SendEmailConfirmationLinkAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var request = await _authService.RequestEmailConfirmationAsync(email, cancellationToken);
        if (!request.MemberExists || request.AlreadyConfirmed ||
            string.IsNullOrWhiteSpace(request.Token))
        {
            return;
        }

        var confirmUrl = $"{Request.Scheme}://{Request.Host}/account/confirm-email" +
            $"?email={Uri.EscapeDataString(email)}" +
            $"&token={Uri.EscapeDataString(request.Token)}";

        try
        {
            var siteName = ResolveSiteName();
            var bodyHtml = await _emailRenderer.RenderAsync(
                viewName: "EmailConfirmation",
                model: new Synergos.CMS.Web.Services.EmailConfirmationEmailModel(
                    DisplayName: request.DisplayName ?? email,
                    ConfirmUrl: confirmUrl,
                    SiteName: siteName),
                cancellationToken);

            await _emailService.SendAsync(new EmailMessage(
                To: email,
                Subject: $"{siteName} · Confirma tu email",
                BodyHtml: bodyHtml),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Email confirmation send failed: email={Email}",
                email);
        }
    }
}
