using System.Text;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Synergos.CMS.Domain.Services;
using Synergos.CMS.Domain.Shared;

namespace Synergos.CMS.Infrastructure.Email;

/// <summary>
/// Transient implementation of <see cref="IFormEmailService"/> that sends emails
/// via Umbraco's <c>IEmailSender</c>. Kept here (Infrastructure) because it depends
/// on a framework-specific transport.
/// </summary>
public sealed class UmbracoFormEmailService(IEmailSender emailSender) : IFormEmailService
{
    public async Task SendFormSubmissionAsync(
        FormDefinitionModel form,
        IReadOnlyDictionary<string, string> data,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(form.RecipientEmail)) return;

        var body = BuildHtmlBody(form, data);
        var subject = $"Nuevo mensaje: {form.FormName}";

        var message = new EmailMessage(
            from:        null,   // usar From configurado en Umbraco
            to:          new[] { form.RecipientEmail },
            cc:          null,
            bcc:         null,
            replyTo:     null,
            subject:     subject,
            body:        body,
            isBodyHtml:  true,
            attachments: null);

        await emailSender.SendAsync(message, "formSubmission");
    }

    private static string BuildHtmlBody(
        FormDefinitionModel form, IReadOnlyDictionary<string, string> data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html><body>");
        sb.AppendLine($"<h2>{System.Net.WebUtility.HtmlEncode(form.FormName)}</h2>");
        sb.AppendLine("<table border='1' cellpadding='8' cellspacing='0'>");
        sb.AppendLine("<thead><tr><th>Campo</th><th>Valor</th></tr></thead><tbody>");

        foreach (var field in form.Fields)
        {
            data.TryGetValue(field.Name, out var value);
            sb.AppendLine($"<tr><td>{System.Net.WebUtility.HtmlEncode(field.Label)}</td>" +
                          $"<td>{System.Net.WebUtility.HtmlEncode(value ?? "")}</td></tr>");
        }

        sb.AppendLine("</tbody></table>");
        sb.AppendLine($"<p><small>Enviado: {DateTime.Now:dd/MM/yyyy HH:mm}</small></p>");
        sb.AppendLine("</body></html>");
        return sb.ToString();
    }
}
