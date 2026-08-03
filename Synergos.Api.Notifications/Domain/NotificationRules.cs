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
}
