using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>
/// Lo que la puerta pone de su parte en cada operación (ADR 0140 F3): quién pide, el negocio del sitio,
/// el contacto del aviso y la llave reemitida. Del navegador no viaja ninguna otra cabecera.
/// </summary>
/// <remarks>
/// <para><b>Los nombres son los de <c>Synergos.Bff.Core.CabecerasDeLaPuerta</c></b>, el otro árbol: se
/// comparte el NOMBRE y no el código, como la correlación, y hay gate que los cruza
/// (<c>PuertaGenericaTests</c>).</para>
///
/// <para><b>Todo es puro</b>: sale de la sesión, de la configuración y de la petición, y no toca la red.
/// Así lo que la puerta afirma por el comprador se puede probar sin levantar nada.</para>
/// </remarks>
public static class LoQuePoneLaPuerta
{
    /// <summary>Quién pide: <c>&lt;kind&gt;:&lt;id&gt;</c>.</summary>
    public const string CabeceraDelSujeto = "X-Synergos-Sujeto";

    /// <summary>Los campos de negocio del sitio que la operación declara: base64url de un JSON.</summary>
    public const string CabeceraDelNegocio = "X-Synergos-Negocio";

    /// <summary>A dónde y a nombre de quién avisar: base64url de <c>{correo, nombre, enlace, sitio}</c>.</summary>
    public const string CabeceraDelContacto = "X-Synergos-Contacto";

    /// <summary>La llave de idempotencia, que la puerta REEMITE.</summary>
    public const string CabeceraDeLaLlave = "Idempotency-Key";

    /// <summary>Las cabeceras que la puerta sabe poner.</summary>
    public static readonly IReadOnlyList<string> Cabeceras = [CabeceraDelSujeto, CabeceraDelNegocio, CabeceraDelContacto];

    /// <summary>El prefijo de una llave reemitida.</summary>
    public const string PrefijoDeLaLlave = "pta-";

    /// <summary>El largo de una llave reemitida: el prefijo y cuarenta hexadecimales.</summary>
    /// <remarks>
    /// Cabe en los 83 que acepta la llave que abre una saga (<c>LlaveDeSaga.MaxLength</c>): el
    /// orquestador cuelga de ella las de cada paso, y la tabla de la puerta revienta al arrancar si una
    /// operación acepta menos.
    /// </remarks>
    public const int LargoDeLaLlave = 44;

    /// <summary>El largo más grande de la llave que manda el navegador.</summary>
    public const int LargoMaximoDeLaLlaveDelNavegador = 128;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>El sujeto de un miembro: el <c>Kind</c> del flujo y su <c>MemberKey</c> en formato <c>n</c>.</summary>
    /// <remarks>El mismo precedente que el comprador de Tienda: con sesión, el comprador ES su miembro.</remarks>
    public static string Sujeto(string kind, Guid miembro) => $"{kind}:{miembro:n}";

    /// <summary>
    /// La llave que la puerta manda al orquestador: <c>pta-</c> y los primeros cuarenta hexadecimales de
    /// SHA-256 de <c>sujeto|flujo|llave del navegador</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Atada al sujeto</b>: la llave del navegador es suya, y con ella reenviada tal cual,
    /// quien conociera la de otro abriría la MISMA saga. Con el sujeto dentro, la misma llave de dos
    /// personas son dos llaves; el reintento de una misma persona sigue siendo el mismo.</para>
    ///
    /// <para>No hace falta HMAC: la llave del navegador es aleatoria y quién es dueño de la saga lo
    /// comprueba el orquestador aparte.</para>
    /// </remarks>
    public static string LlaveReemitida(string sujeto, string flujo, string llave)
        => PrefijoDeLaLlave + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{sujeto}|{flujo}|{llave}")))
            .ToLowerInvariant()[..(LargoDeLaLlave - PrefijoDeLaLlave.Length)];

    /// <summary>
    /// Los campos <paramref name="campos"/> de la configuración de negocio <paramref name="negocio"/>, en
    /// un JSON con sus nombres en camelCase y en base64url. Sólo esos: el resto del negocio no viaja.
    /// </summary>
    public static string Negocio(object negocio, IEnumerable<string> campos)
    {
        ArgumentNullException.ThrowIfNull(negocio);
        var valores = new SortedDictionary<string, object?>(StringComparer.Ordinal);
        foreach (var campo in campos)
        {
            var propiedad = Propiedad(negocio.GetType(), campo)
                ?? throw new InvalidOperationException($"{negocio.GetType().Name} no tiene el campo {campo}.");
            valores[JsonNamingPolicy.CamelCase.ConvertName(propiedad.Name)] = propiedad.GetValue(negocio);
        }
        return Base64Url(JsonSerializer.SerializeToUtf8Bytes(valores, Web));
    }

    /// <summary>La propiedad pública de instancia <paramref name="campo"/> de <paramref name="tipo"/>, sin mirar mayúsculas.</summary>
    public static PropertyInfo? Propiedad(Type tipo, string campo)
        => tipo.GetProperty(campo, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

    /// <summary>El contacto del aviso en base64url. Nunca se registra ni se guarda.</summary>
    public static string Contacto(string correo, string? nombre, string? enlace, string? sitio)
        => Base64Url(JsonSerializer.SerializeToUtf8Bytes(new { correo, nombre, enlace, sitio }, Web));

    /// <summary>
    /// El enlace del aviso: el origen de la petición y la ruta del flujo con sus parámetros puestos.
    /// </summary>
    /// <returns>Nulo sin ruta, o si la ruta pide un parámetro que la operación no trae.</returns>
    public static string? Enlace(string origen, string? ruta, IReadOnlyDictionary<string, string> parametros)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;

        var enlace = ruta;
        foreach (var (nombre, valor) in parametros)
        {
            enlace = enlace.Replace("{" + nombre + "}", Uri.EscapeDataString(valor), StringComparison.Ordinal);
        }
        return enlace.Contains('{', StringComparison.Ordinal) ? null : origen.TrimEnd('/') + enlace;
    }

    /// <summary>Base64 sin relleno y con el alfabeto de URL (RFC 4648 §5).</summary>
    public static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
