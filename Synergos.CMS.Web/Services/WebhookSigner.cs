using System.Security.Cryptography;
using System.Text;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Helper para firmar payloads de webhooks salientes con HMAC-SHA256.
/// El receptor verifica que el payload viene del CMS y no fue
/// alterado en tránsito comparando la firma local con el header
/// <c>X-Synergos-Signature</c>.
/// </summary>
/// <remarks>
/// Convención de firma — la misma para todos los webhook channels
/// (comments moderation, form submission, cart abandonment futuros):
///
/// <code>
/// header: X-Synergos-Signature: sha256={hex_lowercase}
/// signed: HMACSHA256(secret, body) en bytes raw
/// </code>
///
/// El secret vive en *Settings.WebhookHmacSecret. Si está vacío, el
/// canal NO firma (header omitido). Esto permite ramping gradual:
/// integradores nuevos pueden empezar sin firma y agregarla después
/// sin breaking del CMS-side.
///
/// Sin manejo de timestamps (replay protection) en primera versión —
/// el receptor decide si combina con un nonce o un timestamp claim
/// dentro del payload mismo.
/// </remarks>
public static class WebhookSigner
{
    /// <summary>
    /// Nombre canónico del header de firma. Usar como const para
    /// evitar typos en consumers.
    /// </summary>
    public const string SignatureHeaderName = "X-Synergos-Signature";

    /// <summary>
    /// Computa <c>"sha256={hex}"</c> con HMAC-SHA256 sobre <paramref name="body"/>
    /// usando <paramref name="secret"/>. Devuelve null si el secret está
    /// vacío (signaling: "no firmar").
    /// </summary>
    public static string? ComputeHeader(string? secret, ReadOnlySpan<byte> body)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return null;
        }

        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var hashBytes = HMACSHA256.HashData(keyBytes, body);
        var hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        return $"sha256={hex}";
    }
}
