using System.Text.Json;
using Synergos.Api.Notifications.Domain;
using Synergos.Core;

namespace Synergos.Api.Notifications.Transport;

/// <summary>Lo que un evento del proveedor dice, ya traducido a nuestro vocabulario.</summary>
/// <param name="MessageId">A qué mensaje se refiere, en la numeración del proveedor.</param>
/// <param name="Status">A qué estado dice que llegó, o <c>null</c> si no es un evento de estado.</param>
public sealed record ProviderEvent(string? MessageId, DeliveryStatus? Status);

/// <summary>
/// Traduce el cuerpo de un webhook del proveedor a un <see cref="ProviderEvent"/>.
/// </summary>
/// <remarks>
/// <b>Los eventos que no son de estado no son un error.</b> Resend también avisa de aperturas y
/// de clics; esta capacidad no los guarda —quién abrió un correo de marketing es otro problema,
/// con otro régimen de datos personales— pero tampoco puede contestar un 4xx, o el proveedor
/// reintentaría durante días algo que decidimos ignorar a propósito.
/// </remarks>
public static class ProviderEventReader
{
    public static Result<ProviderEvent> Read(string body)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return Rejection.Invalid($"{NotificationRules.CodePrefix}.webhook_unreadable",
                $"El cuerpo del evento no es JSON: {ex.Message}");
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("type", out var tipo)
                || tipo.ValueKind != JsonValueKind.String)
            {
                return Rejection.Invalid($"{NotificationRules.CodePrefix}.webhook_unreadable",
                    "El evento no dice de qué tipo es.");
            }

            string? mensaje = null;
            if (raiz.TryGetProperty("data", out var datos) && datos.ValueKind == JsonValueKind.Object
                && datos.TryGetProperty("email_id", out var id) && id.ValueKind == JsonValueKind.String)
            {
                mensaje = id.GetString();
            }

            return Result.Ok(new ProviderEvent(mensaje, Traducir(tipo.GetString())));
        }
    }

    /// <summary>
    /// El diccionario de eventos, y es corto a propósito.
    /// </summary>
    /// <remarks>
    /// <c>email.sent</c> se traduce a <see cref="DeliveryStatus.Accepted"/> y no a algo que suene
    /// a llegada: el proveedor dice «lo mandé», no «lo recibieron». Confundir esas dos cosas es
    /// exactamente lo que hacía el <c>Sent</c> viejo.
    /// </remarks>
    private static DeliveryStatus? Traducir(string? tipo) => tipo switch
    {
        "email.sent" => DeliveryStatus.Accepted,
        "email.delivered" => DeliveryStatus.Delivered,
        "email.bounced" => DeliveryStatus.Bounced,
        "email.complained" => DeliveryStatus.Complained,
        // email.opened, email.clicked, email.delivery_delayed y lo que venga después: se acusan
        // recibo y no se guardan.
        _ => null,
    };
}
