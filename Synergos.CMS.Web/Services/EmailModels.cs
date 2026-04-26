namespace Synergos.CMS.Web.Services;

/// <summary>
/// View models para los email templates Razor (Ola 82).
/// Records inmutables — el caller los construye y los pasa a
/// <see cref="Synergos.CMS.Interfaces.IEmailTemplateRenderer.RenderAsync"/>.
/// </summary>
public sealed record PasswordResetEmailModel(
    string DisplayName,
    string ResetUrl,
    string SiteName);

public sealed record FormNotificationEmailModel(
    string FormKey,
    IReadOnlyDictionary<string, string> Fields,
    string ClientIp,
    string Referrer,
    DateTime ReceivedAtUtc,
    string SiteName);

public sealed record EmailConfirmationEmailModel(
    string DisplayName,
    string ConfirmUrl,
    string SiteName);
