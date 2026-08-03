using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Synergos.Core;

namespace Synergos.Api.Signing.Domain;

/// <summary>Lo que la firma rechaza <b>sola</b>, y cómo se arma un token.</summary>
public static class SigningRules
{
    public const string CodePrefix = "signing";

    /// <summary>Separador de las partes del token.</summary>
    public const char Separator = '.';

    /// <summary>Vida máxima de una firma.</summary>
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromDays(365);

    /// <summary>Material nuevo para una llave.</summary>
    public static string NewSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>
    /// Arma el token: <c>keyId.expiraUnix.payloadBase64Url.firma</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>El identificador de la llave y el vencimiento van DENTRO de lo firmado.</b> Si
    /// viajaran aparte, cualquiera podría correr el vencimiento hacia adelante o apuntar a otra
    /// llave, y la firma seguiría cuadrando con lo que se firmó originalmente.</para>
    ///
    /// <para>El payload va en base64url y no crudo porque puede contener el separador, y partir
    /// mal el token haría verificar una cosa distinta de la que se firmó.</para>
    /// </remarks>
    public static string Sign(SigningKey key, string payload, DateTimeOffset expiresAt)
    {
        var cuerpo = Armar(key.Id, expiresAt, payload);
        return cuerpo + Separator + Mac(key.Secret, cuerpo);
    }

    /// <summary>Lee un token sin verificarlo. Devuelve <c>false</c> si está mal formado.</summary>
    public static bool TryRead(string? token, out string keyId, out DateTimeOffset expiresAt, out string payload)
    {
        keyId = string.Empty;
        expiresAt = default;
        payload = string.Empty;

        var partes = (token ?? string.Empty).Split(Separator);
        if (partes.Length != 4) return false;
        if (!long.TryParse(partes[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var unix)) return false;

        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(partes[2]));
        }
        catch (FormatException)
        {
            return false;
        }

        keyId = partes[0];
        expiresAt = DateTimeOffset.FromUnixTimeSeconds(unix);
        return true;
    }

    /// <summary>Verifica firma y vigencia.</summary>
    /// <remarks>
    /// <b>La firma se comprueba ANTES del vencimiento.</b> Al revés, un token vencido diría
    /// "vencido" aunque su firma fuera inventada — y eso confirma que el identificador de llave
    /// existe, que es la mitad del trabajo de quien va a atacarla.
    /// </remarks>
    public static Rejection? Verify(SigningKey? key, string token, DateTimeOffset now)
    {
        if (!TryRead(token, out var keyId, out var expiresAt, out var payload))
        {
            return Rejection.Invalid($"{CodePrefix}.malformed_token", "El token no tiene la forma esperada.");
        }
        if (key is null)
        {
            return Rejection.Forbidden($"{CodePrefix}.bad_signature", "La firma no cuadra.");
        }

        var esperada = Mac(key.Secret, Armar(keyId, expiresAt, payload));
        var recibida = token[(token.LastIndexOf(Separator) + 1)..];

        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recibida), Encoding.UTF8.GetBytes(esperada)))
        {
            // El mismo rechazo que para "no existe esa llave": distinguirlos diría qué
            // identificadores de llave son reales.
            return Rejection.Forbidden($"{CodePrefix}.bad_signature", "La firma no cuadra.");
        }

        return now >= expiresAt
            ? Rejection.Expired($"{CodePrefix}.expired", $"La firma venció en {expiresAt:O}.")
            : null;
    }

    /// <summary>Si la vigencia pedida cabe.</summary>
    public static Rejection? CheckLifetime(TimeSpan lifetime)
        => lifetime > TimeSpan.Zero && lifetime <= MaxLifetime
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_lifetime", $"La vigencia va entre 0 y {MaxLifetime}.");

    private static string Armar(string keyId, DateTimeOffset expiresAt, string payload)
        => $"{keyId}{Separator}{expiresAt.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}{Separator}{ToBase64Url(Encoding.UTF8.GetBytes(payload))}";

    private static string Mac(string secret, string cuerpo)
        => ToBase64Url(HMACSHA256.HashData(Convert.FromBase64String(secret), Encoding.UTF8.GetBytes(cuerpo)));

    private static string ToBase64Url(byte[] raw)
        => Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var b = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b.PadRight(b.Length + (4 - b.Length % 4) % 4, '='));
    }
}
