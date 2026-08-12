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

    /// <summary>
    /// Etiqueta que separa el dominio del SELLO del de la firma. Va dentro del MAC.
    /// </summary>
    /// <remarks>
    /// Las dos operaciones usan la MISMA llave, así que sin separación explícita lo único que
    /// impediría que un sello valiera como firma —o al revés— sería que sus cuerpos tengan
    /// forma distinta por casualidad. Depender de esa casualidad es exactamente lo que deja de
    /// ser cierto el día que alguien toque el formato de una de las dos.
    /// </remarks>
    private const string SealDomain = "seal.v1";

    /// <summary>
    /// Sella un contenido: MAC determinista, <b>sin vencimiento</b> y <b>sin payload
    /// recuperable</b>. El resultado es un identificador opaco, no un token que se lee.
    /// </summary>
    /// <remarks>
    /// <para><b>Es una operación distinta de <see cref="Sign"/>, no un parámetro suyo.</b> Un
    /// token de <c>/v1/signatures</c> lleva el vencimiento dentro y el payload en base64url
    /// legible por cualquiera; las dos cosas son correctas para lo que ese endpoint hace y
    /// ninguna sirve para identificar algo permanente. Un diploma no vence, se re-emite igual
    /// las veces que haga falta, y su identificador se imprime y viaja en cada verificación
    /// pública: si el payload fuera recuperable, ese identificador publicaría al titular. Ver
    /// el hallazgo #45.</para>
    ///
    /// <para><b>No se trunca.</b> Recortar el MAC es una decisión sobre dónde se va a imprimir
    /// el valor, y eso lo sabe quien lo imprime, no una capacidad agnóstica.</para>
    /// </remarks>
    public static string Seal(SigningKey key, string payload)
        => Mac(key.Secret, $"{SealDomain}{Separator}{ToBase64Url(Encoding.UTF8.GetBytes(payload))}");

    /// <summary>
    /// Si <paramref name="seal"/> es el sello que le corresponde a <paramref name="payload"/>
    /// bajo <paramref name="key"/>. Comparación en tiempo constante.
    /// </summary>
    /// <remarks>
    /// <b>Se comprueba el sello CONTRA el contenido</b>, no el sello solo. Es la diferencia de
    /// fondo con verificar una firma: acá no se pregunta «¿este valor lo emitimos nosotros?»
    /// sino «¿es este el valor que le toca a este sujeto?». Sin esa segunda pregunta, quien
    /// consiga escribir en el índice de quien consume podría inventar un registro con el
    /// nombre que quiera, y el índice volvería a ser la autoridad.
    /// </remarks>
    public static bool SealMatches(SigningKey key, string payload, string? seal)
    {
        if (string.IsNullOrWhiteSpace(seal)) return false;

        var a = Encoding.UTF8.GetBytes(seal.Trim());
        var b = Encoding.UTF8.GetBytes(Seal(key, payload));
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

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
