using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Synergos.Core;

namespace Synergos.Api.Documents.Domain;

/// <summary>Lo que los documentos rechazan <b>solos</b>, y cómo se firman los enlaces.</summary>
public static class DocumentRules
{
    public const string CodePrefix = "documents";

    /// <summary>Tamaño máximo. Acotado porque el contenido viaja en el cuerpo de la petición.</summary>
    public const long MaxSizeBytes = 10 * 1024 * 1024;

    /// <summary>Vida máxima de un enlace firmado.</summary>
    public static readonly TimeSpan MaxLinkLifetime = TimeSpan.FromHours(24);

    /// <summary>Vida por defecto de un enlace firmado.</summary>
    public static readonly TimeSpan DefaultLinkLifetime = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Tipos admitidos.
    /// </summary>
    /// <remarks>
    /// <b>Lista blanca y no negra.</b> Una lista negra deja pasar todo lo que nadie pensó en
    /// prohibir, y basta un tipo ejecutable servido desde el dominio de la aplicación para que el
    /// navegador de la víctima lo trate como propio. Lo que no esté acá, no entra.
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png", "image/jpeg", "image/webp", "image/gif",
        "text/plain", "text/csv",
        "application/json",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    };

    /// <summary>Si el fichero que llega se puede guardar.</summary>
    public static Rejection? CheckUpload(string? name, string? contentType, byte[]? content)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Rejection.Invalid($"{CodePrefix}.name_required", "El documento necesita un nombre.");
        }
        if (content is null || content.Length == 0)
        {
            return Rejection.Invalid($"{CodePrefix}.empty_content", "El documento viene vacío.");
        }
        if (content.LongLength > MaxSizeBytes)
        {
            return Rejection.Invalid($"{CodePrefix}.too_large",
                $"El documento pesa {content.LongLength} bytes y el tope son {MaxSizeBytes}.");
        }
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType!))
        {
            return Rejection.Invalid($"{CodePrefix}.content_type_not_allowed",
                $"'{contentType}' no está en la lista de tipos admitidos.");
        }
        return null;
    }

    /// <summary>Huella del contenido, en hexadecimal minúscula.</summary>
    public static string Fingerprint(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Firma un enlace de descarga: identificador + vencimiento, con la llave del servicio.
    /// </summary>
    /// <remarks>
    /// El vencimiento entra <b>dentro</b> de lo firmado. Si viajara aparte, cualquiera podría
    /// correrlo hacia adelante y convertir un enlace de quince minutos en uno eterno.
    /// </remarks>
    public static string SignLink(string documentId, DateTimeOffset expiresAt, string signingKey)
    {
        var payload = $"{documentId}|{expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}";
        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(mac).ToLowerInvariant();
    }

    /// <summary>Verifica un enlace firmado.</summary>
    public static Rejection? CheckLink(string documentId, DateTimeOffset expiresAt, string? token,
        string signingKey, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return Rejection.Forbidden($"{CodePrefix}.link_unsigned", "El enlace no viene firmado.");
        }

        var esperado = SignLink(documentId, expiresAt, signingKey);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(token!), Encoding.UTF8.GetBytes(esperado)))
        {
            return Rejection.Forbidden($"{CodePrefix}.link_bad_signature", "La firma del enlace no cuadra.");
        }

        // El vencimiento se comprueba DESPUÉS de la firma: al revés, un enlace vencido diría
        // "vencido" aunque su firma fuera inventada, y eso confirma que el documento existe.
        return now >= expiresAt
            ? Rejection.Expired($"{CodePrefix}.link_expired", $"El enlace venció en {expiresAt:O}.")
            : null;
    }
}
