using System.Text.Json;
using Microsoft.Extensions.Options;
using Synergos.Api.Payments.Domain;
using Synergos.Core;

namespace Synergos.Api.Payments.Transport;

/// <summary>La cabecera con la que Wompi firma un evento.</summary>
/// <param name="Checksum">El <c>X-Event-Checksum</c>.</param>
public sealed record WebhookHeaders(string? Checksum);

/// <summary>
/// Verifica que un evento viene de verdad de Wompi y que no es uno viejo reenviado.
/// </summary>
/// <remarks>
/// <para><b>Un webhook sin verificar es un endpoint público que mueve plata.</b> Cualquiera que
/// sepa la URL podría marcar como pagado un cobro que nunca ocurrió, y el pedido saldría. No es
/// una capa de seguridad de más: es <b>la única que hay</b>, porque este endpoint no puede ir
/// detrás de la llave compartida —quien lo llama es un tercero que no la tiene—.</para>
///
/// <para><b>La lista de propiedades firmadas se LEE del evento, no se cablea.</b> La
/// documentación de Wompi lo advierte con todas las letras: varía por tipo de evento. Un
/// verificador que asuma las tres de hoy empieza a rechazar todo el día que Wompi añada un tipo,
/// y el síntoma —«la firma no corresponde»— apunta al secreto y no a la lista.</para>
///
/// <para><b>Y no se promueve a <c>Synergos.Shared</c></b> aunque <c>Api.Notifications</c> tenga
/// su gemelo: los dos verifican firmas y ahí se acaba el parecido —otro algoritmo, otras
/// cabeceras, otro sitio donde vive el instante—. Promover lo que comparte la forma y no el
/// contenido deja una abstracción con dos ramas y ningún dueño.</para>
/// </remarks>
public sealed class WebhookVerifier
{
    /// <summary>Cuánto se admite de desfase entre la firma y ahora.</summary>
    /// <remarks>
    /// Sin ventana, una petición capturada sirve para siempre: el atacante no necesita falsificar
    /// nada, solo repetir un evento legítimo que grabó — y repetir un <c>APPROVED</c> es cobrar
    /// dos veces a ojos del sistema. Cinco minutos, y estirarla «por si los relojes» es estirar
    /// exactamente eso.
    /// </remarks>
    public static readonly TimeSpan Tolerancia = TimeSpan.FromMinutes(5);

    private readonly WompiOptions _options;
    private readonly TimeProvider _clock;

    public WebhookVerifier(IOptions<WompiOptions> options, TimeProvider clock)
    {
        _options = options.Value;
        _clock = clock;
    }

    /// <summary>Si el cuerpo que llegó está firmado por Wompi y es reciente.</summary>
    public Rejection? Verify(WebhookHeaders headers, string body)
    {
        if (string.IsNullOrWhiteSpace(_options.EventsSecret))
        {
            // Sin secreto NO se acepta nada. La alternativa —aceptar todo mientras no se
            // configure— convierte un olvido de despliegue en un endpoint abierto que da cobros
            // por buenos.
            return Rejection.Forbidden($"{PaymentRules.CodePrefix}.webhook_not_configured",
                "No hay secreto de eventos configurado: no se puede verificar quién manda esto.");
        }

        if (string.IsNullOrWhiteSpace(headers.Checksum))
        {
            return Rejection.Forbidden($"{PaymentRules.CodePrefix}.webhook_unsigned",
                "Al evento le falta la cabecera de firma.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException ex)
        {
            return Rejection.Invalid($"{PaymentRules.CodePrefix}.webhook_unreadable",
                $"El cuerpo del evento no es JSON: {ex.Message}");
        }

        using (doc)
        {
            var raiz = doc.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("timestamp", out var instante)
                || !instante.TryGetInt64(out var segundos))
            {
                return Rejection.Forbidden($"{PaymentRules.CodePrefix}.webhook_unsigned",
                    "El evento no trae el instante en el que se firmó.");
            }

            var desfase = _clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(segundos);

            // Se mira en los dos sentidos: un futuro lejano también es sospechoso, y además es la
            // forma de que un reloj mal puesto se note en vez de ampliar la ventana en silencio.
            if (desfase > Tolerancia || desfase < -Tolerancia)
            {
                return Rejection.Forbidden($"{PaymentRules.CodePrefix}.webhook_replay",
                    $"El evento se firmó fuera de la ventana de {Tolerancia.TotalMinutes:0} minutos.");
            }

            if (!raiz.TryGetProperty("signature", out var firma)
                || firma.ValueKind != JsonValueKind.Object
                || !firma.TryGetProperty("properties", out var propiedades)
                || propiedades.ValueKind != JsonValueKind.Array)
            {
                return Rejection.Forbidden($"{PaymentRules.CodePrefix}.webhook_unsigned",
                    "El evento no dice qué propiedades firmó.");
            }

            raiz.TryGetProperty("data", out var datos);

            var valores = propiedades
                .EnumerateArray()
                .Select(p => p.ValueKind == JsonValueKind.String ? Resolver(datos, p.GetString()) : null)
                .ToList();

            var esperado = WompiSignature.EventChecksum(valores, segundos, _options.EventsSecret!);

            // Se compara contra la CABECERA y no contra `signature.checksum`: el cuerpo entero lo
            // escribe quien llama, así que cotejar el cuerpo consigo mismo no prueba nada. Lo que
            // prueba algo es que el valor coincida con el que solo se puede calcular con el
            // secreto.
            return WompiSignature.ChecksumMatches(esperado, headers.Checksum)
                ? null
                : Rejection.Forbidden($"{PaymentRules.CodePrefix}.webhook_signature_invalid",
                    "La firma del evento no corresponde.");
        }
    }

    /// <summary>
    /// Resuelve una ruta con puntos —<c>transaction.status</c>— contra el <c>data</c> del evento.
    /// </summary>
    /// <remarks>
    /// Un número se toma con su texto CRUDO y no reformateado: lo que Wompi firmó son los
    /// caracteres que mandó, así que convertirlo a <c>long</c> y de vuelta a texto cambiaría
    /// <c>1.0</c> por <c>1</c> y la firma dejaría de cuadrar por una razón invisible. Y una
    /// propiedad ausente concatena vacío: la ausencia es parte del mensaje firmado.
    /// </remarks>
    private static string? Resolver(JsonElement datos, string? ruta)
    {
        if (string.IsNullOrWhiteSpace(ruta) || datos.ValueKind != JsonValueKind.Object) return null;

        var actual = datos;
        foreach (var tramo in ruta.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (actual.ValueKind != JsonValueKind.Object || !actual.TryGetProperty(tramo, out actual))
            {
                return null;
            }
        }

        return actual.ValueKind switch
        {
            JsonValueKind.String => actual.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => actual.GetRawText(),
        };
    }
}
