using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IMemberRosterWriter"/> implementado sobre
/// <see cref="IMemberService"/> de Umbraco.
/// </summary>
/// <remarks>
/// Operaciones ejecutadas sync (IMemberService no expone async para
/// estos paths en Umbraco 13). Wrapped en Task.FromResult para
/// satisfacer el contrato async-friendly del seam.
/// </remarks>
public sealed class UmbracoMemberRosterWriter : IMemberRosterWriter
{
    private readonly IMemberService _memberService;
    private readonly IMemberAuthService _memberAuth;
    private readonly IEmailService _emailService;
    private readonly RazorEmailTemplateRenderer _emailRenderer;
    private readonly IBrandingProvider _branding;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<UmbracoMemberRosterWriter> _logger;

    public UmbracoMemberRosterWriter(
        IMemberService memberService,
        IMemberAuthService memberAuth,
        IEmailService emailService,
        RazorEmailTemplateRenderer emailRenderer,
        IBrandingProvider branding,
        IHttpContextAccessor httpContextAccessor,
        ILogger<UmbracoMemberRosterWriter> logger)
    {
        _memberService = memberService;
        _memberAuth = memberAuth;
        _emailService = emailService;
        _emailRenderer = emailRenderer;
        _branding = branding;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public Task<bool> LockAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        if (member is null)
        {
            return Task.FromResult(false);
        }

        if (!member.IsLockedOut)
        {
            member.IsLockedOut = true;
            _memberService.Save(member);
            _logger.LogInformation(
                "Admin locked member key={Key} email={Email}",
                memberKey, member.Email);
        }
        return Task.FromResult(true);
    }

    public Task<bool> UnlockAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        if (member is null)
        {
            return Task.FromResult(false);
        }

        if (member.IsLockedOut)
        {
            member.IsLockedOut = false;
            // Reset failed login attempts cuando se desbloquea — el
            // counter se preservaría sino y el siguiente fail re-bloquea.
            member.FailedPasswordAttempts = 0;
            _memberService.Save(member);
            _logger.LogInformation(
                "Admin unlocked member key={Key} email={Email}",
                memberKey, member.Email);
        }
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        if (member is null)
        {
            return Task.FromResult(false);
        }

        var email = member.Email;
        _memberService.Delete(member);
        _logger.LogWarning(
            "Admin hard-deleted member key={Key} email={Email}",
            memberKey, email);
        return Task.FromResult(true);
    }

    public async Task<bool> SendPasswordResetAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        if (member is null || string.IsNullOrWhiteSpace(member.Email))
        {
            return false;
        }

        var result = await _memberAuth.RequestPasswordResetAsync(member.Email, cancellationToken);
        if (!result.EmailExists || string.IsNullOrWhiteSpace(result.Token))
        {
            _logger.LogWarning(
                "Admin password-reset for member key={Key} email={Email} returned EmailExists=false",
                memberKey, member.Email);
            return false;
        }

        // Reusa el mismo template que /account/forgot-password (PasswordReset.cshtml)
        // — UX consistency con el self-service flow (ADR 0044).
        var ctx = _httpContextAccessor.HttpContext;
        var scheme = ctx?.Request?.Scheme ?? "https";
        var host = ctx?.Request?.Host.Value ?? "localhost";
        var resetUrl = $"{scheme}://{host}/account/reset-password" +
            $"?email={Uri.EscapeDataString(member.Email)}" +
            $"&token={Uri.EscapeDataString(result.Token)}";

        var brand = _branding.GetCurrent();
        var siteName = string.IsNullOrWhiteSpace(brand.DisplayName) ? "Synergos" : brand.DisplayName;
        var bodyHtml = await _emailRenderer.RenderAsync(
            viewName: "PasswordReset",
            model: new PasswordResetEmailModel(
                DisplayName: result.DisplayName ?? member.Email,
                ResetUrl: resetUrl,
                SiteName: siteName),
            cancellationToken);

        await _emailService.SendAsync(new EmailMessage(
            To: member.Email,
            Subject: $"{siteName} · Restablece tu contraseña",
            BodyHtml: bodyHtml),
            cancellationToken);

        _logger.LogInformation(
            "Admin requested password reset for member key={Key} email={Email}",
            memberKey, member.Email);
        return true;
    }

    public Task<bool> SetRolesAsync(
        Guid memberKey,
        IReadOnlyCollection<string> roleNames,
        CancellationToken cancellationToken)
    {
        var member = _memberService.GetByKey(memberKey);
        if (member is null)
        {
            return Task.FromResult(false);
        }

        var current = (_memberService.GetAllRoles(member.Id) ?? Array.Empty<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var target = roleNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var toAdd = target.Where(r => !current.Contains(r)).ToArray();
        var toRemove = current.Where(r => !target.Contains(r)).ToArray();

        if (toAdd.Length > 0)
        {
            _memberService.AssignRoles(new[] { member.Id }, toAdd);
        }
        if (toRemove.Length > 0)
        {
            _memberService.DissociateRoles(new[] { member.Id }, toRemove);
        }

        if (toAdd.Length + toRemove.Length > 0)
        {
            _logger.LogInformation(
                "Admin set roles for member key={Key} email={Email} added=[{Added}] removed=[{Removed}]",
                memberKey, member.Email,
                string.Join(',', toAdd), string.Join(',', toRemove));
        }
        return Task.FromResult(true);
    }
}
