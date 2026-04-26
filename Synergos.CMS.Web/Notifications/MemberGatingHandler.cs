using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Routing;
using Umbraco.Extensions;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Aplica las reglas de <c>compMemberGating.requiresAuth</c> +
/// <c>allowedRolesCsv</c> al request actual. Si el miembro actual no
/// puede ver la página, redirige a la página de login con returnUrl.
/// </summary>
/// <remarks>
/// Hook: <see cref="RoutingRequestNotification"/> — dispara cuando
/// Umbraco resuelve el routing a un <c>IPublishedContent</c>. Es el
/// punto correcto para corto-circuitar el render: no se ejecuta la
/// view ni controllers; se devuelve redirect 302 al login.
///
/// El path de login se lee de
/// <see cref="MembersSettings.LoginPath"/> (default <c>/login</c>;
/// configurable vía <c>Synergos:Members:LoginPath</c>).
///
/// Fail-open por seguridad: si la página no compone
/// <c>compMemberGating</c> o <c>requiresAuth=false</c>, este handler
/// no hace nada — el request sigue normal.
/// </remarks>
public sealed class MemberGatingHandler : INotificationHandler<RoutingRequestNotification>
{
    private const string RequiresAuthAlias = "requiresAuth";
    private const string AllowedRolesCsvAlias = "allowedRolesCsv";

    private readonly IMemberAccessGate _memberAccessGate;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<MemberGatingHandler> _logger;
    private readonly string _loginPath;

    public MemberGatingHandler(
        IMemberAccessGate memberAccessGate,
        IHttpContextAccessor httpContextAccessor,
        IOptions<MembersSettings> membersSettings,
        ILogger<MemberGatingHandler> logger)
    {
        _memberAccessGate = memberAccessGate;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        var settings = membersSettings.Value;
        _loginPath = string.IsNullOrWhiteSpace(settings.LoginPath) ? "/login" : settings.LoginPath;
    }

    public void Handle(RoutingRequestNotification notification)
    {
        var requestBuilder = notification.RequestBuilder;
        var content = requestBuilder.PublishedContent;
        if (content is null)
        {
            return;
        }

        if (!content.HasProperty(RequiresAuthAlias) || !content.Value<bool>(RequiresAuthAlias))
        {
            return;
        }

        var allowedRoles = content.HasProperty(AllowedRolesCsvAlias)
            ? content.Value<string>(AllowedRolesCsvAlias)
            : null;

        if (_memberAccessGate.HasAnyRole(allowedRoles))
        {
            return;
        }

        var currentUrl = _httpContextAccessor.HttpContext?.Request?.Path.Value ?? content.Url();
        var redirectTarget = $"{_loginPath}?returnUrl={Uri.EscapeDataString(currentUrl)}";

        _logger.LogInformation(
            "Member gating: redirecting unauthenticated request from {ContentUrl} to {LoginPath}",
            currentUrl,
            _loginPath);

        requestBuilder.SetRedirect(redirectTarget);
    }
}
