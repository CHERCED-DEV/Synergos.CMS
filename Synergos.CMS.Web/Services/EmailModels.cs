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

public sealed record CommentPendingModerationEmailModel(
    int NodeId,
    string CommentId,
    string AuthorName,
    string Body,
    DateTime CreatedAtUtc,
    string ModerationQueueUrl,
    string SiteName);

public sealed record CartAbandonmentEmailModel(
    string CartId,
    int ItemCount,
    decimal Subtotal,
    string Currency,
    DateTime LastActivityUtc,
    int MinutesSinceActivity,
    string SiteName);
