namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Email</c>. Identidad del remitente default usado por
/// <c>IEmailService</c> cuando el caller no override From explícito.
/// </summary>
/// <remarks>
/// La config de SMTP (host, port, credentials) NO vive aquí — vive
/// en <c>Umbraco:CMS:Global:Smtp</c> que Umbraco usa internamente
/// vía su propio <c>IEmailSender</c>. Esta POCO solo gobierna la
/// identidad del remitente default y settings que no son
/// transport-specific.
/// </remarks>
public sealed class EmailSettings
{
    /// <summary>
    /// Email del remitente default (ej. <c>noreply@example.com</c>).
    /// Si el sitio no override esta config, los emails saldrán con un
    /// From que el destinatario puede no reconocer.
    /// </summary>
    public string FromAddress { get; init; } = "noreply@synergos.local";

    /// <summary>
    /// Nombre visible del remitente default. Default <c>"Synergos"</c>.
    /// Aparece en clientes de email como "Synergos &lt;noreply@…&gt;".
    /// </summary>
    public string FromName { get; init; } = "Synergos";

    /// <summary>
    /// Si true, el handler de envío logea Warning cuando Umbraco SMTP
    /// no está configurado. Default true. En sitios donde el envío de
    /// email es fire-and-forget opcional (ej. notificaciones de form
    /// submission no críticas), el operador puede silenciar.
    /// </summary>
    public bool WarnOnMissingSmtp { get; init; } = true;
}
