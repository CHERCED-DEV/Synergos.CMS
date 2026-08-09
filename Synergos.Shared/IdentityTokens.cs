using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Synergos.Core;

namespace Synergos.Shared;

/// <summary>
/// Lo que un token de identidad afirma: quién actúa, con qué roles, y hasta cuándo.
/// </summary>
/// <param name="Subject">Quién. Opaco, como en todo el árbol de servicios.</param>
/// <param name="Roles">Qué puede hacer, según lo dijo <c>Api.Identity</c> al emitir.</param>
/// <param name="IssuedAtUtc">Cuándo se emitió este token.</param>
/// <param name="ExpiresAtUtc">Hasta cuándo vale.</param>
/// <param name="SessionStartedAtUtc">
/// Cuándo empezó la SESIÓN, que no es lo mismo que cuándo se emitió este token.
/// </param>
/// <remarks>
/// <para><b><see cref="SessionStartedAtUtc"/> es el techo de la renovación</b>, y sin él los 15
/// minutos no significarían nada: un token robado se renovaría para siempre, quince minutos cada
/// vez. Con él, la sesión entera tiene un final aunque cada token sea corto.</para>
///
/// <para><b>Los roles viajan DENTRO del token</b> y no se consultan por request. Es la mitad de
/// lo que hace que una capacidad pueda verificar sin llamar a nadie — y tiene su costo, dicho de
/// frente: revocar un rol tarda lo que quede de vigencia del token. Quince minutos es
/// precisamente el precio que se aceptó por no convertir a <c>Api.Identity</c> en el punto único
/// de fallo de las veinte capacidades.</para>
/// </remarks>
public sealed record IdentityClaims(
    Ref Subject,
    IReadOnlyList<string> Roles,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset SessionStartedAtUtc);

/// <summary>Por qué un token no sirve. <c>null</c> significa que sí sirve.</summary>
public enum TokenFailure
{
    /// <summary>No parsea, o la firma no cuadra.</summary>
    Malformed,

    /// <summary>El <c>kid</c> no corresponde a ninguna llave conocida.</summary>
    UnknownKey,

    /// <summary>Venció.</summary>
    Expired,
}

/// <summary>
/// Emite y verifica los tokens de identidad — <b>el único formato, para las veinte</b>.
/// </summary>
/// <remarks>
/// <para><b>Por qué vive en <c>Synergos.Shared</c> desde el primer día</b>, contra la regla de
/// promover al segundo consumidor (<c>CLAUDE.md</c> §17). La regla existe para no inventar
/// abstracciones especulativas, y acá no hay especulación: el token existe <i>para que una
/// capacidad lo verifique sin llamar a nadie</i>. Ponerlo en <c>Api.Identity</c> obligaría a que
/// las otras capacidades la referenciaran, y eso está prohibido de plano (§11: ninguna referencia
/// de ensamblado cruza capas). No hay un sitio válido con un solo consumidor.</para>
///
/// <para><b>Verificación LOCAL, y ésa es la decisión de fondo</b> (HU #14 §3.2). Llamar a
/// <c>Api.Identity</c> en cada petición la convertiría en el punto único de fallo de las veinte —
/// y es la peor candidata posible, porque corre sobre fichero JSON con <c>lock</c> de proceso.
/// Con verificación local, <c>Api.Identity</c> caída significa «no entran sesiones nuevas», no
/// «se para todo».</para>
///
/// <para><b>Lo que este token NO es, dicho antes de que alguien lo suponga.</b> Lo emite un
/// servicio nuestro a partir de la palabra del CMS (camino (b) de la HU), así que <b>no es prueba
/// más fuerte frente a un tercero</b> que la sesión del CMS: la cadena de confianza toca fondo en
/// el mismo sitio. Lo que sí compra es integridad interna — una capacidad deja de creerle al
/// llamador quién está actuando, porque el sujeto viene firmado y no se puede reapuntar. El
/// escalón probatorio de verdad es <c>GovFederation</c>, y está fuera de alcance.</para>
///
/// <para><b>El <c>kid</c> va desde el primer día aunque haya UNA sola llave.</b> Rotar sin él
/// obliga a invalidar todos los tokens a la vez, y añadirlo después es el mismo trabajo con los
/// tokens ya emitidos en contra.</para>
///
/// <para>Se reusa la convención de firma que ya está probada en este repo (HMAC-SHA256, hex
/// minúscula, comparación en tiempo constante) — la misma de <c>HmacTicketSigner</c>.</para>
/// </remarks>
public sealed class IdentityTokens
{
    /// <summary>Prefijo de versión del formato. Cambia el día que cambie la forma.</summary>
    public const string Version = "v1";

    /// <summary>Prefijo de los códigos de rechazo. Los pone la CAPACIDAD, nunca el orquestador.</summary>
    public const string CodePrefix = "identity";

    /// <summary>Cabecera por la que viaja el token.</summary>
    /// <remarks>
    /// Propia y no <c>Authorization</c>: por ahí ya viaja —o viajará— lo que autentique al
    /// borde público, y mezclarlas haría que quitar una tumbara la otra sin que se note.
    /// </remarks>
    public const string HeaderName = "X-Synergos-Identity";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IReadOnlyDictionary<string, byte[]> _keys;
    private readonly string _activeKid;

    /// <param name="keys">Las llaves conocidas por su <c>kid</c>. Verifica con todas.</param>
    /// <param name="activeKid">Con cuál se FIRMA. Rotar es añadir una y mover esto.</param>
    /// <remarks>
    /// <b>Se verifica con todas y se firma con una</b>: es lo que permite rotar sin invalidar lo
    /// que ya está en manos de la gente. Durante la rotación conviven la vieja —que solo
    /// verifica— y la nueva, hasta que vence el último token firmado con la vieja.
    /// </remarks>
    public IdentityTokens(IReadOnlyDictionary<string, byte[]> keys, string activeKid)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            throw new ArgumentException("Sin llaves no se puede ni firmar ni verificar.", nameof(keys));
        }
        if (string.IsNullOrWhiteSpace(activeKid) || !keys.ContainsKey(activeKid))
        {
            throw new ArgumentException(
                $"La llave activa '{activeKid}' no está entre las conocidas.", nameof(activeKid));
        }
        _keys = keys;
        _activeKid = activeKid;
    }

    /// <summary>Emite un token para el sujeto dado.</summary>
    public string Issue(IdentityClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var carga = Codificar(JsonSerializer.SerializeToUtf8Bytes(Payload.From(claims), Json));
        var cuerpo = $"{Version}.{_activeKid}.{carga}";
        return $"{cuerpo}.{Firmar(cuerpo, _keys[_activeKid])}";
    }

    /// <summary>
    /// Verifica el token y devuelve lo que afirma, o el motivo por el que no vale.
    /// </summary>
    /// <remarks>
    /// <b>Se comprueba la firma ANTES que el vencimiento</b>, y el orden no es casual: decirle a
    /// quien trae un token fabricado que «venció» le confirma que el formato es correcto y le
    /// ahorra la mitad del trabajo de adivinar el resto.
    /// </remarks>
    public (IdentityClaims? Claims, TokenFailure? Failure) Verify(string? raw, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(raw)) return (null, TokenFailure.Malformed);

        var partes = raw.Split('.');
        if (partes.Length != 4 || !string.Equals(partes[0], Version, StringComparison.Ordinal))
        {
            return (null, TokenFailure.Malformed);
        }

        if (!_keys.TryGetValue(partes[1], out var llave)) return (null, TokenFailure.UnknownKey);

        var cuerpo = $"{partes[0]}.{partes[1]}.{partes[2]}";
        var esperada = Firmar(cuerpo, llave);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(esperada), Encoding.UTF8.GetBytes(partes[3])))
        {
            return (null, TokenFailure.Malformed);
        }

        Payload? carga;
        try { carga = JsonSerializer.Deserialize<Payload>(Decodificar(partes[2]), Json); }
        catch (Exception ex) when (ex is JsonException or FormatException) { return (null, TokenFailure.Malformed); }

        var claims = carga?.ToClaims();
        if (claims is null) return (null, TokenFailure.Malformed);

        return now >= claims.ExpiresAtUtc ? (null, TokenFailure.Expired) : (claims, null);
    }

    /// <summary>El rechazo que corresponde a cada motivo, con su código.</summary>
    /// <remarks>
    /// <b>Los cuatro son <see cref="Rejection.Invalid"/> y no <c>Unauthorized</c></b>: la llave
    /// compartida ya decidió que quien llama es de los nuestros. Que el token no sirva es un
    /// problema de la petición, no de quién la manda — y el 401 está reservado para la llave,
    /// que es la única que dice «no sos de acá».
    /// </remarks>
    public static Rejection ToRejection(TokenFailure failure) => failure switch
    {
        TokenFailure.Expired => Rejection.Invalid($"{CodePrefix}.token_expired",
            "El token de identidad venció. Hay que renovarlo antes de seguir."),
        TokenFailure.UnknownKey => Rejection.Invalid($"{CodePrefix}.token_unknown_key",
            "El token viene firmado con una llave que este servicio no conoce."),
        _ => Rejection.Invalid($"{CodePrefix}.token_malformed",
            "El token de identidad no se pudo leer o su firma no cuadra."),
    };

    /// <summary>
    /// El rechazo de cuando el token dice una persona y la petición dice otra.
    /// </summary>
    /// <remarks>
    /// <b>Es el que da sentido a toda la HU #14.</b> Sin esta comprobación, una capacidad sigue
    /// creyendo el <c>who</c> que le mandan y el token solo sería decoración.
    /// </remarks>
    public static Rejection SubjectMismatch(Ref enElToken, Ref enLaPeticion)
        => Rejection.Invalid($"{CodePrefix}.token_subject_mismatch",
            $"El token identifica a {enElToken} y la petición actúa como {enLaPeticion}.");

    private static string Firmar(string cuerpo, byte[] llave)
        => Convert.ToHexString(HMACSHA256.HashData(llave, Encoding.UTF8.GetBytes(cuerpo))).ToLowerInvariant();

    // Base64 URL-safe: el token viaja en una cabecera y puede acabar en una URL de depuración.
    // Con '+' y '/' sin escapar, lo segundo lo rompe en silencio.
    private static string Codificar(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decodificar(string s)
    {
        var b64 = s.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '='));
    }

    /// <summary>La forma serializada. Nombres cortos porque van en cada petición.</summary>
    private sealed record Payload(
        string SubKind, string SubId, IReadOnlyList<string> Roles, long Iat, long Exp, long Sst)
    {
        public static Payload From(IdentityClaims c) => new(
            c.Subject.Kind, c.Subject.Id, c.Roles,
            c.IssuedAtUtc.ToUnixTimeSeconds(), c.ExpiresAtUtc.ToUnixTimeSeconds(),
            c.SessionStartedAtUtc.ToUnixTimeSeconds());

        public IdentityClaims? ToClaims()
        {
            var sujeto = Ref.TryCreate(SubKind, SubId);
            return sujeto is null
                ? null
                : new IdentityClaims(sujeto, Roles ?? Array.Empty<string>(),
                    DateTimeOffset.FromUnixTimeSeconds(Iat),
                    DateTimeOffset.FromUnixTimeSeconds(Exp),
                    DateTimeOffset.FromUnixTimeSeconds(Sst));
        }
    }
}
