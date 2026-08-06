namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam de envío de email transaccional (password reset, form
/// notifications, registration confirmation). Aísla a consumidores
/// del backend concreto (SMTP, SendGrid, Mailgun, Umbraco's IEmailSender).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.DefaultEmailService</c> y delega a
/// <c>Umbraco.Cms.Core.Mail.IEmailSender</c> — Umbraco ya gestiona
/// SMTP config (Umbraco:CMS:Global:Smtp) + pickup directory para dev.
///
/// Para producción con provider dedicado (SendGrid API, Mailgun, etc.):
/// swap del binding sin tocar consumidores.
/// </remarks>
public interface IEmailService
{
    /// <summary>
    /// Envía un email transaccional. Si la infra SMTP no está
    /// configurada y no hay pickup directory, el envío falla
    /// silenciosamente (logged como Warning).
    /// </summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>
/// Mensaje de email a enviar.
/// </summary>
/// <param name="To">Destinatario(s) — si múltiples, separados por
///   coma.</param>
/// <param name="Subject">Asunto del email.</param>
/// <param name="BodyHtml">Cuerpo HTML.</param>
/// <param name="BodyText">Cuerpo texto plano alternativo. Si null, se
///   genera del BodyHtml strip-tags básico.</param>
/// <param name="From">Override del remitente. Si null, usa
///   <c>EmailSettings.FromAddress</c> + <c>FromName</c>.</param>
/// <param name="ReplyTo">Override del Reply-To header. Útil para
///   forms de contacto (responder al visitante directamente).</param>
public sealed record EmailMessage(
    string To,
    string Subject,
    string BodyHtml,
    string? BodyText = null,
    string? From = null,
    string? ReplyTo = null);
