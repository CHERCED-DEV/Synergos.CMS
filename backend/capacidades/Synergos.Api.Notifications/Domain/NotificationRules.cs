using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
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

    /// <summary>Con qué se codifica un valor que entra a un cuerpo HTML.</summary>
    /// <remarks>
    /// <c>UnicodeRanges.All</c> y no <c>HtmlEncoder.Default</c>, que sólo deja pasar ASCII: los dos
    /// se ven igual en el correo, pero el de por defecto vuelve entidad cada tilde y cada eñe de un
    /// valor, y el rastro guardado pasa de «rechazó» a «rechaz&amp;#xF3;». Medido contra
    /// <c>WebUtility.HtmlEncode</c>, que hace lo mismo con todo el Latin-1 (#175).
    /// </remarks>
    private static readonly HtmlEncoder CodificadorHtml = HtmlEncoder.Create(UnicodeRanges.All);

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
    /// Si el cuerpo de una plantilla de ese canal es HTML. <b>El asunto no lo es en ninguno.</b>
    /// </summary>
    /// <remarks>
    /// En correo, el cuerpo sale como <c>html</c> hacia el proveedor; en SMS y push es el texto que
    /// se lee tal cual, y codificarlo le mandaría «&amp;amp;» a un teléfono.
    /// </remarks>
    public static bool BodyIsHtml(Channel channel) => channel == Channel.Email;

    /// <summary>
    /// Rellena los marcadores <c>{nombre}</c> del asunto y del cuerpo de una plantilla, cada uno
    /// según lo que es. Un marcador sin valor <b>rechaza</b>.
    /// </summary>
    /// <remarks>
    /// <para>Dejarlo crudo mandaría "Hola {nombre}" a una persona; sustituirlo por vacío mandaría
    /// "Hola ,". Las dos son peores que no mandar y decir por qué.</para>
    ///
    /// <para><b>Lo que trae la plantilla es marcado; lo que llega en los valores, texto.</b> La
    /// plantilla la escribe el dominio y se publica una vez; los valores llegan en cada envío y
    /// pueden venir de cualquiera —un nombre, una dirección, el error que devolvió un tercero—.
    /// Por eso en un cuerpo HTML cada valor entra <b>codificado</b>: crudo, un <c>&lt;</c> o un
    /// <c>&amp;</c> en un valor es marcado dentro de un correo con el remitente del producto
    /// (#175). El asunto no se toca: es una cabecera del correo, texto plano que ningún cliente
    /// interpreta, y codificarlo le mostraría «&amp;lt;» a quien lo lee.</para>
    ///
    /// <para><b>Recibe la plantilla entera y no un texto suelto, a propósito.</b> Con un texto
    /// suelto, saber si era un asunto o un cuerpo HTML le tocaba a quien llamaba — y un llamador
    /// que se olvida es el defecto de nuevo. Acá lo decide el canal de la plantilla.</para>
    ///
    /// <para><b>No hay forma de meter marcado de confianza por un valor</b>, y ninguna plantilla
    /// lo necesita hoy. El día que haga falta, va declarado en el contrato de la plantilla, no
    /// como excepción de esta regla. Lo mismo un valor que vaya dentro de un <c>href</c>:
    /// codificar protege el texto y los atributos entre comillas, pero un <c>javascript:</c>
    /// codificado sigue siendo un enlace.</para>
    /// </remarks>
    public static Result<(string Subject, string Body)> Fill(
        Template plantilla, IReadOnlyDictionary<string, string> valores)
    {
        var asunto = Rellenar(plantilla.Subject, valores, html: false);
        if (!asunto.IsOk) return asunto.Rejection!;

        var cuerpo = Rellenar(plantilla.Body, valores, html: BodyIsHtml(plantilla.Channel));
        if (!cuerpo.IsOk) return cuerpo.Rejection!;

        return Result.Ok((asunto.Value, cuerpo.Value));
    }

    private static Result<string> Rellenar(string texto, IReadOnlyDictionary<string, string> valores, bool html)
    {
        string? faltante = null;
        var salida = Marcador.Replace(texto, m =>
        {
            var nombre = m.Groups[1].Value;
            if (valores.TryGetValue(nombre, out var v)) return html ? CodificadorHtml.Encode(v) : v;
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
