using System.Text.Json;
using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lo que un webhook de pago trae, ya normalizado: cada PSP lo dice a su manera.
/// </summary>
/// <param name="EventId">Llave de deduplicación.</param>
/// <param name="OrderReference">Referencia del comercio.</param>
/// <param name="SessionId">Sesión/transacción en el PSP.</param>
/// <param name="OccurredUtc">Cuándo lo emitió el PSP.</param>
/// <param name="SignedValues">Valores que el PSP declara haber firmado, EN
///   ORDEN. Vacío cuando el esquema no firma por propiedades.</param>
/// <param name="Checksum">Checksum declarado, si el esquema lo trae en el body.</param>
public sealed record PaymentWebhookPayload(
    string EventId,
    string OrderReference,
    string SessionId,
    DateTimeOffset OccurredUtc,
    IReadOnlyList<string?> SignedValues,
    string? Checksum);

/// <summary>
/// Traduce el cuerpo crudo de cada PSP a <see cref="PaymentWebhookPayload"/>.
/// </summary>
/// <remarks>
/// Aparte del controller porque cada PSP nuevo agrega un caso acá y nada más.
/// Devuelve <c>null</c> ante cualquier cosa que no entienda: el receptor
/// responde 400 y no intenta adivinar.
/// </remarks>
public static class PaymentWebhookPayloadReader
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static PaymentWebhookPayload? Read(string provider, byte[] body)
    {
        if (body is null || body.Length == 0) return null;

        try
        {
            return provider.ToLowerInvariant() switch
            {
                "wompi" => ReadWompi(body),
                _ => ReadStub(body),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Esquema propio del stub: plano y explícito.</summary>
    private static PaymentWebhookPayload? ReadStub(byte[] body)
    {
        var p = JsonSerializer.Deserialize<StubShape>(body, Json);
        if (p is null
            || string.IsNullOrWhiteSpace(p.EventId)
            || string.IsNullOrWhiteSpace(p.SessionId)
            || string.IsNullOrWhiteSpace(p.OrderRef))
        {
            return null;
        }
        return new PaymentWebhookPayload(
            p.EventId, p.OrderRef, p.SessionId,
            DateTimeOffset.UtcNow, Array.Empty<string?>(), null);
    }

    /// <summary>
    /// Esquema de Wompi: <c>{ event, data.transaction{...}, signature{properties,
    /// checksum}, timestamp, sent_at }</c>.
    /// </summary>
    /// <remarks>
    /// <para>Las propiedades firmadas se resuelven <b>leyendo la lista que el
    /// propio evento declara</b> en <c>signature.properties</c> y navegando el
    /// JSON por esa ruta. La documentación de Wompi advierte explícitamente que
    /// no se hardcodee: varía por tipo de evento. Hardcodearla habría funcionado
    /// con <c>transaction.updated</c> y roto los eventos de Nequi y Bancolombia.</para>
    ///
    /// <para>Wompi no expone un id de evento propio, así que se usa el de la
    /// transacción combinado con el timestamp: dos eventos sobre la misma
    /// transacción en momentos distintos son hechos distintos, y el mismo
    /// reenviado es el mismo.</para>
    /// </remarks>
    private static PaymentWebhookPayload? ReadWompi(byte[] body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("transaction", out var tx))
        {
            return null;
        }

        var txId = tx.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        var reference = tx.TryGetProperty("reference", out var refEl) ? refEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(txId) || string.IsNullOrWhiteSpace(reference))
        {
            return null;
        }

        var timestamp = root.TryGetProperty("timestamp", out var tsEl) && tsEl.TryGetInt64(out var ts)
            ? ts
            : 0L;

        var signedValues = new List<string?>();
        string? checksum = null;
        if (root.TryGetProperty("signature", out var sig))
        {
            if (sig.TryGetProperty("checksum", out var ckEl))
            {
                checksum = ckEl.GetString();
            }
            if (sig.TryGetProperty("properties", out var props)
                && props.ValueKind == JsonValueKind.Array)
            {
                foreach (var prop in props.EnumerateArray())
                {
                    signedValues.Add(ResolvePath(root, prop.GetString()));
                }
            }
        }

        return new PaymentWebhookPayload(
            EventId: $"{txId}:{timestamp}",
            OrderReference: reference,
            SessionId: reference,   // en Web Checkout la referencia ES la sesión
            OccurredUtc: timestamp > 0
                ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
                : DateTimeOffset.UtcNow,
            SignedValues: signedValues,
            Checksum: checksum);
    }

    /// <summary>
    /// Resuelve una ruta con puntos (<c>"transaction.amount_in_cents"</c>) contra
    /// el JSON del evento. Devuelve <c>null</c> si algún tramo no existe — y esa
    /// ausencia concatena vacío en el checksum, que es lo que Wompi espera.
    /// </summary>
    private static string? ResolvePath(JsonElement root, string? dottedPath)
    {
        if (string.IsNullOrWhiteSpace(dottedPath)) return null;

        // Las rutas de signature.properties nacen bajo "data".
        if (!root.TryGetProperty("data", out var current)) return null;

        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next))
            {
                return null;
            }
            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.String => current.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => current.GetRawText(),
        };
    }

    /// <summary>Verifica el checksum de un evento de Wompi contra el secreto de eventos.</summary>
    public static bool WompiChecksumValid(PaymentWebhookPayload payload, string? eventsSecret)
    {
        if (string.IsNullOrWhiteSpace(eventsSecret) || string.IsNullOrWhiteSpace(payload.Checksum))
        {
            return false;
        }
        var timestamp = payload.OccurredUtc.ToUnixTimeSeconds();
        var expected = WompiSignature.EventChecksum(payload.SignedValues, timestamp, eventsSecret);
        return WompiSignature.ChecksumMatches(expected, payload.Checksum);
    }

    private sealed record StubShape(string? EventId, string? SessionId, string? OrderRef, string? Status);
}
