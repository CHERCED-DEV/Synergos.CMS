using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Synergos.Api.Notifications.Domain;
using Synergos.Core;

namespace Synergos.Api.Notifications.Transport;

/// <summary>
/// Los tres encabezados con los que un proveedor firma un webhook.
/// </summary>
/// <param name="Id">Identificador del evento. <b>Es la llave de idempotencia</b>: el proveedor
/// reentrega el mismo evento hasta ver un 2xx, y el nuestro no distingue reentrega de novedad.</param>
/// <param name="Timestamp">Cuándo lo firmó, en segundos desde época.</param>
/// <param name="Signature">Una o más firmas, separadas por espacio, cada una <c>v1,&lt;base64&gt;</c>.</param>
public sealed record WebhookHeaders(string? Id, string? Timestamp, string? Signature);

/// <summary>
/// Verifica que un webhook viene de verdad del proveedor y que no es uno viejo reenviado.
/// </summary>
/// <remarks>
/// <para><b>Un webhook sin verificar es un endpoint público que cambia el estado de los datos.</b>
/// Cualquiera que sepa la URL podría marcar como entregado un aviso que rebotó — y en Gobierno,
/// «entregado» es lo que hace correr un término. No es una capa de seguridad de más: es la
/// única que hay, porque este endpoint no puede ir detrás de la llave compartida (quien lo llama
/// es un tercero que no la tiene).</para>
///
/// <para><b>Esto se diseñó junto con el receptor de PSE</b> (HU 6b) aunque solo se construya
/// acá: los dos necesitan las mismas cuatro cosas —firma, antirrepetición, idempotencia por id
/// del proveedor y llegada fuera de orden— y decidirlas dos veces produce dos formas distintas
/// de estar mal. <b>No se promueve a <c>Synergos.Shared</c> todavía</b>: sería promover al
/// primer consumidor, la misma regla que hizo esperar a <c>Bff.Core</c> (doc 10).</para>
/// </remarks>
public sealed class WebhookVerifier
{
    /// <summary>Cuánto se admite de desfase entre la firma y ahora.</summary>
    /// <remarks>
    /// Sin ventana, una petición capturada sirve para siempre: el atacante no necesita falsificar
    /// nada, solo repetir una firma legítima que grabó. Cinco minutos es lo que usa el propio
    /// proveedor, y estirarla «por si acaso los relojes» es estirar exactamente eso.
    /// </remarks>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    private readonly ResendOptions _options;
    private readonly TimeProvider _clock;

    public WebhookVerifier(IOptions<ResendOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Si el cuerpo que llegó está firmado por el proveedor y es reciente.</summary>
    public Rejection? Verify(WebhookHeaders headers, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.WebhookSecret))
        {
            // Sin secreto NO se acepta nada. La alternativa —aceptar todo mientras no se
            // configure— convierte un olvido de despliegue en un endpoint abierto que escribe.
            return Rejection.Forbidden($"{NotificationRules.CodePrefix}.webhook_not_configured",
                "No hay secreto de webhook configurado: no se puede verificar quién manda esto.");
        }

        if (string.IsNullOrWhiteSpace(headers.Id)
            || string.IsNullOrWhiteSpace(headers.Timestamp)
            || string.IsNullOrWhiteSpace(headers.Signature))
        {
            return Rejection.Forbidden($"{NotificationRules.CodePrefix}.webhook_unsigned",
                "Al evento le faltan los encabezados de firma.");
        }

        if (!long.TryParse(headers.Timestamp, out var segundos))
        {
            return Rejection.Forbidden($"{NotificationRules.CodePrefix}.webhook_unsigned",
                "El instante de la firma no es un número.");
        }

        var firmado = DateTimeOffset.FromUnixTimeSeconds(segundos);
        var desfase = _clock.GetUtcNow() - firmado;
        // Se mira en los dos sentidos: un futuro lejano también es sospechoso, y además es la
        // forma de que un reloj mal puesto se note en vez de ampliar la ventana en silencio.
        if (desfase > Tolerancia || desfase < -Tolerancia)
        {
            return Rejection.Forbidden($"{NotificationRules.CodePrefix}.webhook_replay",
                $"El evento se firmó fuera de la ventana de {Tolerancia.TotalMinutes:0} minutos.");
        }

        var esperada = Firmar(_options.WebhookSecret!, headers.Id!, headers.Timestamp!, body);

        // El proveedor puede mandar varias firmas a la vez durante una rotación de secreto. Basta
        // que una coincida — y se comparan TODAS en tiempo constante, sin cortar en la primera.
        var coincide = false;
        foreach (var parte in headers.Signature!.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = parte.IndexOf(',');
            var valor = i >= 0 ? parte[(i + 1)..] : parte;
            coincide |= FijoEnTiempo(valor, esperada);
        }

        return coincide
            ? null
            : Rejection.Forbidden($"{NotificationRules.CodePrefix}.webhook_signature_invalid",
                "La firma del evento no corresponde.");
    }

    /// <summary>La firma que debería traer: HMAC-SHA256 sobre <c>{id}.{instante}.{cuerpo}</c>.</summary>
    /// <remarks>
    /// El id y el instante entran en lo firmado a propósito: si solo se firmara el cuerpo, dos
    /// eventos idénticos serían indistinguibles y una repetición pasaría la verificación.
    /// </remarks>
    public static string Firmar(string secreto, string id, string timestamp, string body)
    {
        // El secreto viaja como `whsec_<base64>`; lo que se usa como llave son los bytes, no el
        // texto. Firmar con el texto da una firma que nunca coincide y un fallo que parece de red.
        var crudo = secreto.StartsWith("whsec_", StringComparison.Ordinal) ? secreto["whsec_".Length..] : secreto;
        var llave = Convert.FromBase64String(crudo);

        using var hmac = new HMACSHA256(llave);
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{id}.{timestamp}.{body}")));
    }

    /// <summary>Comparación sin atajo temprano — comparar firmas con <c>==</c> filtra el secreto a plazos.</summary>
    private static bool FijoEnTiempo(string a, string b)
    {
        var x = Encoding.UTF8.GetBytes(a);
        var y = Encoding.UTF8.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(x, y);
    }
}
