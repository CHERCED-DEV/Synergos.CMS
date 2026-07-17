using System.Security.Cryptography;
using System.Text;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Helper para firmar payloads de webhooks salientes con HMAC-SHA256
/// + replay protection via timestamp. Patrón canónico tipo Stripe/GitHub.
/// </summary>
/// <remarks>
/// Convención de firma — la misma para todos los webhook channels
/// (comments / forms / cart):
///
/// <code>
/// header: X-Synergos-Timestamp: {iso8601_utc}
/// header: X-Synergos-Signature: sha256={hex_lowercase}
/// signed: HMACSHA256(secret, "{timestamp}.{body}")
/// </code>
///
/// El timestamp es PARTE del input del HMAC, no solo header
/// separado — incluirlo previene replay attacks donde un atacante
/// reusa un body+signature válidos con un timestamp nuevo.
///
/// Receptor validates en este orden:
/// <list type="number">
///   <item>Lee headers <c>X-Synergos-Timestamp</c> y <c>X-Synergos-Signature</c>.</item>
///   <item>Recomputa HMAC sobre <c>"{timestamp}.{body}"</c> con su copia del secret.</item>
///   <item>Compara constant-time contra el header recibido (rechaza mismatch).</item>
///   <item>Verifica <c>|now - timestamp| &lt; tolerance_window</c> (típicamente ±5 min).</item>
/// </list>
///
/// El secret vive en <c>*Settings.WebhookHmacSecret</c>. Si está vacío, el
/// canal NO firma (headers omitidos). Esto permite ramping gradual.
/// </remarks>
public static class WebhookSigner
{
    /// <summary>Header con timestamp ISO 8601 UTC del momento de firma.</summary>
    public const string TimestampHeaderName = "X-Synergos-Timestamp";

    /// <summary>Header con la firma <c>"sha256={hex}"</c> del HMAC.</summary>
    public const string SignatureHeaderName = "X-Synergos-Signature";

    /// <summary>
    /// Computa los 2 headers (timestamp + signature) que el caller agrega
    /// al request. Devuelve null si el secret está vacío (signaling
    /// "no firmar"), en cuyo caso ambos headers se omiten.
    /// </summary>
    /// <param name="secret">Secret compartido con el receptor.</param>
    /// <param name="body">Body del request en bytes raw.</param>
    /// <returns>Tuple (timestamp ISO 8601, signature header value) o null.</returns>
    public static (string Timestamp, string Signature)? ComputeSignedHeaders(
        string? secret,
        ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var timestamp = DateTime.UtcNow.ToString("O");
        var keyBytes = Encoding.UTF8.GetBytes(secret);

        // signedInput = "{timestamp}.{body_utf8}"
        var timestampBytes = Encoding.UTF8.GetBytes(timestamp + ".");
        var signedInput = new byte[timestampBytes.Length + body.Length];
        timestampBytes.CopyTo(signedInput.AsSpan());
        body.CopyTo(signedInput.AsSpan(timestampBytes.Length));

        var hashBytes = HMACSHA256.HashData(keyBytes, signedInput);
        var hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return (timestamp, $"sha256={hex}");
    }
}
