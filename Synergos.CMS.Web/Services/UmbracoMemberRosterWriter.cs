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
    private readonly ILogger<UmbracoMemberRosterWriter> _logger;

    public UmbracoMemberRosterWriter(
        IMemberService memberService,
        ILogger<UmbracoMemberRosterWriter> logger)
    {
        _memberService = memberService;
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
}
