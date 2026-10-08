using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Core;

namespace Synergos.Api.Notifications.Transport;

/// <summary>
/// Deja cada correo como un <c>.eml</c> en <see cref="PickupOptions.Directory"/>. Sólo en Development.
/// </summary>
/// <remarks>
/// <para><b><c>SmtpClient</c> con <c>SpecifiedPickupDirectory</c>, sin paquetes.</b> Medido en .NET
/// 10.0.12: escribe un <c>.eml</c> completo (destinatario, asunto en UTF-8, cuerpo HTML) sin abrir una
/// conexión. Un paquete MIME sería una dependencia más para lo mismo, y el correo que sale de acá no
/// va a ningún buzón.</para>
///
/// <para><b>Como cualquier transporte, sólo traduce</b>: el cuerpo llega rellenado y con cada valor
/// codificado por <see cref="NotificationRules.Fill"/>; no se decora ni se codifica otra vez.</para>
///
/// <para>Lleva <c>X-Synergos-Envio</c> con la llave del envío, para encontrar en la carpeta el
/// <c>.eml</c> de un envío concreto sin mirar el log.</para>
/// </remarks>
public sealed class PickupNotificationSender : INotificationSender
{
    /// <summary>La cabecera con la llave del envío.</summary>
    public const string CabeceraDelEnvio = "X-Synergos-Envio";

    private readonly PickupOptions _options;

    public PickupNotificationSender(IOptions<PickupOptions> options) => _options = options.Value;

    /// <summary>Sólo correo: un SMS en una carpeta no se verifica de ninguna forma útil.</summary>
    public bool Supports(Channel channel) => channel == Channel.Email;

    public async Task<Result<string>> SendAsync(
        Channel channel, string address, string subject, string body,
        string idempotencyKey, CancellationToken ct = default)
    {
        if (channel != Channel.Email) return Result.Rejected<string>(NotificationRules.ChannelUnsupported(channel));
        if (!_options.IsConfigured) return Result.Rejected<string>(NotificationRules.TransportNotConfigured(channel));

        var carpeta = Path.GetFullPath(_options.Directory!);
        System.IO.Directory.CreateDirectory(carpeta);

        using var correo = new MailMessage(_options.From, address)
        {
            Subject = subject,
            SubjectEncoding = Encoding.UTF8,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            BodyTransferEncoding = TransferEncoding.Base64,
            IsBodyHtml = true,
            HeadersEncoding = Encoding.UTF8,
        };
        correo.Headers.Add(CabeceraDelEnvio, idempotencyKey);

        using var smtp = new SmtpClient
        {
            DeliveryMethod = SmtpDeliveryMethod.SpecifiedPickupDirectory,
            PickupDirectoryLocation = carpeta,
        };
        try
        {
            await smtp.SendMailAsync(correo, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is SmtpException or IOException or UnauthorizedAccessException)
        {
            // «No lanza», como todo transporte: una carpeta que no se puede escribir es un proveedor
            // que no atiende, y el envío queda para reintentarse en vez de tumbar la petición.
            return Result.Rejected<string>(NotificationRules.TransportUnavailable(ex.GetType().Name));
        }

        return Result.Ok($"pickup:{idempotencyKey}");
    }
}
