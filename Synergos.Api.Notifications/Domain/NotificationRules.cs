using System.Text.RegularExpressions;
using Synergos.Core;

namespace Synergos.Api.Notifications.Domain;

/// <summary>Lo que los avisos rechazan <b>solos</b>.</summary>
public static class NotificationRules
{
    public const string CodePrefix = "notifications";

    /// <summary>Envíos por destinatario dentro de <see cref="RateWindow"/>.</summary>
    public const int MaxPerRecipient = 20;

    /// <summary>Ventana del tope de frecuencia.</summary>
    public static readonly TimeSpan RateWindow = TimeSpan.FromHours(1);

    private static readonly Regex Marcador = new(@"\{(\w+)\}", RegexOptions.Compiled);

    /// <summary>Si la dirección sirve para ese canal.</summary>
    /// <remarks>
    /// Comprobación deliberadamente básica: validar correos "de verdad" con una expresión es un
    /// clásico que rechaza direcciones legítimas. Lo que sí atrapa esto es el error real y
    /// frecuente —mandar un teléfono al canal de correo, o al revés— que si no se ve acá, se ve
    /// como un envío "entregado" que nadie recibió.
    /// </remarks>
    public static Rejection? CheckAddress(Channel channel, string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return Rejection.Invalid($"{CodePrefix}.address_required", "Hace falta una dirección de destino.");
        }

        var ok = channel switch
        {
            Channel.Email => address.Contains('@', StringComparison.Ordinal) && !address.Contains(' ', StringComparison.Ordinal),
            Channel.Sms => address.Count(char.IsDigit) >= 7,
            Channel.Push => address.Length >= 8,
            _ => false,
        };

        return ok
            ? null
            : Rejection.Invalid($"{CodePrefix}.address_channel_mismatch",
                $"'{address}' no parece una dirección de {channel}.");
    }

    /// <summary>Si el destinatario no pasó el tope de frecuencia.</summary>
    /// <remarks>
    /// Sin tope, un lazo con un fallo manda mil correos a la misma persona antes de que nadie lo
    /// note — y el costo no es solo la factura: es la dirección del remitente marcada como spam,
    /// que deja sin avisos a todos los demás.
    /// </remarks>
    public static Rejection? CheckRate(int enviadosEnVentana)
        => enviadosEnVentana < MaxPerRecipient
            ? null
            : Rejection.Conflict($"{CodePrefix}.rate_limited",
                $"Ya se enviaron {enviadosEnVentana} avisos a este destinatario en {RateWindow}.");

    /// <summary>
    /// Rellena los marcadores <c>{nombre}</c>. Un marcador sin valor <b>rechaza</b>.
    /// </summary>
    /// <remarks>
    /// Dejarlo crudo mandaría "Hola {nombre}" a una persona; sustituirlo por vacío mandaría
    /// "Hola ,". Las dos son peores que no mandar y decir por qué.
    /// </remarks>
    public static Result<string> Fill(string plantilla, IReadOnlyDictionary<string, string> valores)
    {
        string? faltante = null;
        var salida = Marcador.Replace(plantilla, m =>
        {
            var nombre = m.Groups[1].Value;
            if (valores.TryGetValue(nombre, out var v)) return v;
            faltante ??= nombre;
            return m.Value;
        });

        return faltante is null
            ? Result.Ok(salida)
            : Rejection.Invalid($"{CodePrefix}.missing_placeholder",
                $"La plantilla usa {{{faltante}}} y no vino ese valor.");
    }

    // ── El avance del estado ────────────────────────────────────────────────

    /// <summary>
    /// Cuánto ha avanzado un envío. <b>No es el valor del enum</b>, y esa distinción es el punto.
    /// </summary>
    /// <remarks>
    /// Lo que se guarda es <i>qué tan lejos llegó</i>, no <i>qué evento llegó último</i>. Los
    /// eventos del proveedor llegan por red y no vienen ordenados: <c>delivered</c> puede llegar
    /// antes que <c>accepted</c>. Con «último gana», ese par deja el envío en «aceptado» para
    /// siempre — un envío que sí llegó, marcado como que quizá no.
    /// </remarks>
    private static int Rank(DeliveryStatus s) => s switch
    {
        DeliveryStatus.Queued => 0,
        DeliveryStatus.Accepted => 1,
        // Los tres desenlaces del transporte empatan: son excluyentes entre sí, y al empatar,
        // el primero que llegó es el que queda. Un rebote posterior a una entrega no existe.
        DeliveryStatus.Delivered => 2,
        DeliveryStatus.Bounced => 2,
        // Rendirse empata con los desenlaces del transporte: es final. Y por encima de Queued,
        // para que un evento tardío del proveedor no resucite un envío ya abandonado.
        DeliveryStatus.GivenUp => 2,
        DeliveryStatus.Failed => 2,
        // La queja es lo único que sí ocurre DESPUÉS de haber llegado.
        DeliveryStatus.Complained => 3,
        _ => 0,
    };

    /// <summary>
    /// A qué estado queda un envío cuando llega un evento del proveedor.
    /// </summary>
    /// <remarks>
    /// <b>Nunca retrocede.</b> Devolver el actual cuando el evento no aporta es la respuesta
    /// correcta y no un error: reintentar la entrega de un webhook es lo normal, y el proveedor
    /// tiene que ver un 2xx o va a seguir insistiendo durante días.
    /// </remarks>
    public static DeliveryStatus Advance(DeliveryStatus actual, DeliveryStatus llega)
        => Rank(llega) > Rank(actual) ? llega : actual;

    // ── Lo que agrega un transporte de verdad ───────────────────────────────

    /// <summary>El proveedor no respondió o falló por su lado. <b>Transitorio: se reintenta.</b></summary>
    /// <remarks>
    /// <c>Unavailable</c> y no <c>Conflict</c> a propósito: <see cref="Rejection.IsTransient"/> es
    /// lo que ya decide en <c>Bff.Core</c> si algo se reintenta o se grita una vez. Clasificarlo
    /// mal no produce un mensaje feo — produce un aviso que nunca sale, o una tormenta de
    /// peticiones contra un proveedor que ya dijo que no.
    /// </remarks>
    public static Rejection TransportUnavailable(string detalle)
        => Rejection.Unavailable($"{CodePrefix}.transport_unavailable", $"El proveedor no pudo atender el envío: {detalle}");

    /// <summary>El proveedor rechazó la dirección o el contenido. No se reintenta.</summary>
    public static Rejection TransportRejected(string detalle)
        => Rejection.Invalid($"{CodePrefix}.transport_rejected", $"El proveedor rechazó el envío: {detalle}");

    /// <summary>Falta la credencial del canal. Se grita una vez y no se reintenta.</summary>
    public static Rejection TransportNotConfigured(Channel channel)
        => Rejection.Invalid($"{CodePrefix}.transport_not_configured",
            $"No hay credenciales configuradas para {channel}: el aviso NO salió y no va a salir hasta que se configuren.");

    /// <summary>Este despliegue no tiene proveedor para ese canal.</summary>
    public static Rejection ChannelUnsupported(Channel channel)
        => Rejection.Invalid($"{CodePrefix}.channel_unsupported",
            $"Este despliegue no tiene transporte para {channel}.");
}
