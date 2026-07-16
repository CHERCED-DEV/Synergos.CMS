using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Verificador de la firma HMAC de un webhook de pago ENTRANTE (T3, doc 25). Es el
/// espejo INVERSO de <see cref="WebhookSigner"/> (esquema stub/synergos): recomputa
/// <c>HMACSHA256(secret, "{timestamp}.{body}")</c>, compara constant-time contra el
/// header recibido y valida la ventana de replay. Permite simular webhooks offline
/// (firmar con <see cref="WebhookSigner"/>, verificar aquí) sin un PSP real. La firma
/// ES la autorización — el PSP postea server-to-server sin cookie de member.
/// </summary>
/// <remarks>
/// Los adapters reales (Ola B, ej. Wompi con su <c>X-Event-Checksum</c> SHA256) añaden
/// su propio esquema; hoy hay uno solo, así que es un helper estático (no un seam —
/// evita abstracción prematura, se generaliza cuando exista el 2º esquema).
/// </remarks>
public static class PaymentWebhookVerifier
{
    /// <summary>Ventana de tolerancia de replay por defecto (±5 min).</summary>
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public enum Result
    {
        /// <summary>Firma válida (o no exigida por secret vacío en demo) → procesar.</summary>
        Ok,
        /// <summary>Faltan headers de firma cuando el secret los exige → 400.</summary>
        MissingHeaders,
        /// <summary>Firma no coincide → 401.</summary>
        InvalidSignature,
        /// <summary>Timestamp fuera de la ventana de replay → 401.</summary>
        Expired,
    }

    /// <summary>
    /// Verifica la firma del webhook. <paramref name="secret"/> vacío = no se exige firma
    /// (demo): devuelve <see cref="Result.Ok"/>. Con secret, exige y valida los headers.
    /// El <paramref name="body"/> son los bytes RAW del request (el HMAC es sobre bytes
    /// exactos — deserializar/reserializar rompería la firma).
    /// </summary>
    public static Result Verify(
        string? secret,
        string? timestampHeader,
        string? signatureHeader,
        ReadOnlySpan<byte> body,
        TimeSpan? tolerance = null)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return Result.Ok;   // demo: sin secret configurado no se exige firma
        }
        if (string.IsNullOrWhiteSpace(timestampHeader) || string.IsNullOrWhiteSpace(signatureHeader))
        {
            return Result.MissingHeaders;
        }

        // signatureHeader = "sha256={hex}". Extraer el hex y parsear a bytes.
        var hex = signatureHeader.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase)
            ? signatureHeader["sha256=".Length..]
            : signatureHeader;
        byte[] provided;
        try { provided = Convert.FromHexString(hex.Trim()); }
        catch (FormatException) { return Result.InvalidSignature; }

        // Recomputar HMAC sobre "{timestamp}.{body}".
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var prefix = Encoding.UTF8.GetBytes(timestampHeader + ".");
        var signedInput = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signedInput.AsSpan());
        body.CopyTo(signedInput.AsSpan(prefix.Length));
        var computed = HMACSHA256.HashData(keyBytes, signedInput);

        // Constant-time — nunca == de string (evita timing attack).
        if (provided.Length != computed.Length || !CryptographicOperations.FixedTimeEquals(provided, computed))
        {
            return Result.InvalidSignature;
        }

        // Ventana de replay: el timestamp es parte del input firmado, así que un
        // atacante no puede reusar body+firma con un timestamp nuevo.
        if (!DateTimeOffset.TryParse(timestampHeader, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var ts))
        {
            return Result.InvalidSignature;
        }
        if (DateTimeOffset.UtcNow - ts > (tolerance ?? DefaultTolerance) || ts - DateTimeOffset.UtcNow > (tolerance ?? DefaultTolerance))
        {
            return Result.Expired;
        }

        return Result.Ok;
    }
}
