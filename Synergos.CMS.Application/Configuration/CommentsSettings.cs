namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Comments</c>. Gobierna persistencia + política de
/// moderación + límites del módulo de comentarios.
/// </summary>
public sealed class CommentsSettings
{
    /// <summary>
    /// Subdirectorio bajo <c>ContentRootPath</c> donde se persisten
    /// los JSON de comentarios (uno por nodo). Default
    /// <c>"App_Data/syn-comments/"</c> — fuera de wwwroot, no
    /// servido por static-files.
    /// </summary>
    public string StorageRoot { get; init; } = "App_Data/syn-comments/";

    /// <summary>
    /// Longitud máxima del cuerpo del comentario en chars. Default
    /// 2000. Defensa contra payloads abusivos.
    /// </summary>
    public int MaxBodyLengthChars { get; init; } = 2000;

    /// <summary>
    /// Si true, comentarios nuevos quedan con <see cref="Comment.Approved"/>
    /// false y NO aparecen en el render hasta que un moderator los
    /// aprueba (manualmente vía edición del JSON o adapter custom).
    /// Default false — los sitios sin moderación tienen comments
    /// visibles inmediatamente. Para sitios con valor alto, activar.
    /// </summary>
    public bool RequireModeration { get; init; } = false;

    /// <summary>
    /// Máximo de comentarios por hora desde la misma IP. Default 5.
    /// Reusa el patrón sliding-window de Forms (ADR 0030).
    /// </summary>
    public int MaxCommentsPerHourPerIp { get; init; } = 5;

    /// <summary>
    /// Si true, requiere que el visitante esté autenticado vía
    /// <see cref="Synergos.CMS.Interfaces.IMemberAccessGate"/> para
    /// poder comentar. Default true — más seguro y trazable.
    /// </summary>
    public bool RequireAuthentication { get; init; } = true;

    /// <summary>
    /// Dirección de email a la que se notifica cuando un comentario
    /// queda pendiente de moderación (consumido por la impl default
    /// de <see cref="Synergos.CMS.Interfaces.ICommentModerationNotifier"/>).
    /// Default null/empty = notifier es no-op (no envía email). Solo
    /// efectivo si <see cref="RequireModeration"/> es true.
    /// </summary>
    public string? NotifyEmailAddress { get; init; }

    /// <summary>
    /// URL HTTP(S) a la que el <c>WebhookCommentModerationNotifier</c>
    /// hace POST con el payload JSON del comentario pendiente. Compatible
    /// con Slack incoming webhooks, Discord webhooks, Microsoft Teams
    /// connectors, n8n, Zapier o cualquier endpoint custom. Default
    /// null/empty = canal no-op.
    /// </summary>
    public string? WebhookUrl { get; init; }

    /// <summary>
    /// Bearer token opcional. Si está poblado, el webhook adjunta
    /// header <c>Authorization: Bearer {token}</c> al POST. Para
    /// Slack/Discord/Teams típicamente vacío (la URL ya contiene el
    /// secreto); para endpoints custom protegidos, requerido.
    /// </summary>
    public string? WebhookBearerToken { get; init; }

    /// <summary>
    /// Secret compartido para firmar el body del webhook con HMAC-SHA256.
    /// Si está poblado, el canal adjunta header
    /// <c>X-Synergos-Signature: sha256={hex}</c>. El receptor recomputa
    /// HMAC sobre el body recibido y compara para verificar autenticidad.
    /// Default vacío — opt-in. Sin replay protection built-in (combinar
    /// con timestamp/nonce dentro del payload si se necesita).
    /// </summary>
    public string? WebhookHmacSecret { get; init; }

    /// <summary>
    /// URL de Slack incoming webhook. Si está poblada, el canal
    /// <c>SlackCommentModerationNotifier</c> POST con payload formato
    /// Slack Block Kit (header + section + fields + action button).
    /// Independiente de <see cref="WebhookUrl"/> — un sitio puede
    /// tener ambos y los 2 disparan por cada evento.
    /// </summary>
    public string? SlackWebhookUrl { get; init; }

    /// <summary>
    /// URL de Discord incoming webhook. Si está poblada, el canal
    /// <c>DiscordCommentModerationNotifier</c> POST con payload
    /// formato Discord embeds (color brand + fields inline + timestamp).
    /// </summary>
    public string? DiscordWebhookUrl { get; init; }

    /// <summary>
    /// URL de Microsoft Teams incoming webhook (Office 365 connector).
    /// Si está poblada, el canal <c>TeamsCommentModerationNotifier</c>
    /// POST con payload formato MessageCard (sections + facts +
    /// activityTitle).
    /// </summary>
    public string? TeamsWebhookUrl { get; init; }
}
