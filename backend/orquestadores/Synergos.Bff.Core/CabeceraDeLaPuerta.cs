using System.Buffers.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Synergos.Core;

namespace Synergos.Bff.Core;

/// <summary>
/// Metadato de un endpoint: lee una cabecera que pone la PUERTA del CMS, no el navegador (ADR 0140 F3).
/// </summary>
/// <param name="Nombre">La cabecera.</param>
/// <param name="Requerida">Si sin ella el endpoint rechaza.</param>
/// <remarks>
/// <para><b>Lo que pone la puerta viaja en cabeceras, y el cuerpo del navegador pasa tal cual</b>
/// (decisión 3 del plan de la F3): así hay UN contrato público y la puerta no parsea ni muta cuerpos.
/// El contrato publica estas cabeceras con <c>x-synergos-puerta: true</c>, el UI las omite de su tipo
/// —no las manda el navegador— y una sonda contra el host comprueba que la declarada sea la que el
/// endpoint lee.</para>
///
/// <para>Se leen ANTES que cualquier otra regla del endpoint, como la llave de idempotencia: una
/// cabecera ilegible se rechaza con su código (<c>&lt;dominio&gt;.&lt;cabecera&gt;_invalido</c>) y
/// no según lo que el resto de la petición traiga.</para>
/// </remarks>
public sealed record CabeceraDeLaPuerta(string Nombre, bool Requerida);

/// <summary>Las cabeceras que pone la puerta, y cómo se declaran y se leen.</summary>
public static class CabecerasDeLaPuerta
{
    /// <summary>
    /// Quién compra, cita o viaja: <c>&lt;kind&gt;:&lt;id&gt;</c> en texto plano. Lo pone la puerta
    /// desde la sesión —el <c>Kind</c> de la configuración del flujo, el <c>MemberKey</c> de quien
    /// entró— y el navegador no lo puede escribir: la puerta tira toda cabecera que él mande.
    /// </summary>
    public const string Sujeto = "X-Synergos-Sujeto";

    /// <summary>
    /// La configuración de negocio del sitio que la operación necesita (ADR 0137): base64url de un
    /// JSON con SÓLO los campos que la puerta declara para el flujo —hoy, la comisión—.
    /// </summary>
    public const string Negocio = "X-Synergos-Negocio";

    /// <summary>
    /// Adónde avisar y a nombre de quién: base64url de <c>{correo, nombre, enlace, sitio}</c>. Sólo en
    /// la fase que avisa; es efímero y no se guarda.
    /// </summary>
    public const string Contacto = "X-Synergos-Contacto";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    /// <summary>Declara en un endpoint que lee la cabecera <paramref name="nombre"/> de la puerta.</summary>
    public static RouteHandlerBuilder ConCabeceraDeLaPuerta(
        this RouteHandlerBuilder builder, string nombre, bool requerida = false)
        => builder.WithMetadata(new CabeceraDeLaPuerta(nombre, requerida));

    /// <summary>
    /// El código con que se rechaza una cabecera de la puerta ilegible:
    /// <c>X-Synergos-Contacto</c> → <c>&lt;prefijo&gt;.contacto_invalido</c>.
    /// </summary>
    public static string CodigoInvalido(string prefijo, string nombre)
        => $"{prefijo}.{nombre["X-Synergos-".Length..].ToLowerInvariant()}_invalido";

    /// <summary>El código con que se rechaza una cabecera de la puerta que hace falta y no vino.</summary>
    public static string CodigoRequerido(string prefijo, string nombre)
        => $"{prefijo}.{nombre["X-Synergos-".Length..].ToLowerInvariant()}_requerido";

    /// <summary>Lee el sujeto: <c>&lt;kind&gt;:&lt;id&gt;</c>.</summary>
    /// <returns><c>true</c> con <paramref name="sujeto"/> nulo si no viene; <c>true</c> con el sujeto
    /// si se lee; <c>false</c> si viene y no es un <see cref="Ref"/>.</returns>
    public static bool TryLeerSujeto(HttpRequest http, out Ref? sujeto)
    {
        ArgumentNullException.ThrowIfNull(http);
        sujeto = null;
        if (!http.Headers.TryGetValue(Sujeto, out var crudo) || crudo.Count == 0) return true;
        if (crudo.Count != 1 || crudo[0] is not { } valor) return false;

        var corte = valor.IndexOf(':', StringComparison.Ordinal);
        sujeto = corte > 0 ? Ref.TryCreate(valor[..corte], valor[(corte + 1)..]) : null;
        return sujeto is not null;
    }

    /// <summary>
    /// Lee un JSON en base64url de la cabecera <paramref name="nombre"/>.
    /// </summary>
    /// <returns><c>true</c> con <paramref name="valor"/> nulo si no viene; <c>true</c> con el valor si
    /// se lee; <c>false</c> si viene y no se puede leer —base64url o JSON rotos, o repetida—.</returns>
    public static bool TryLeerJson<T>(HttpRequest http, string nombre, out T? valor) where T : class
    {
        ArgumentNullException.ThrowIfNull(http);
        valor = null;
        if (!http.Headers.TryGetValue(nombre, out var crudo) || crudo.Count == 0) return true;
        if (crudo.Count != 1 || string.IsNullOrWhiteSpace(crudo[0])) return false;

        try
        {
            valor = JsonSerializer.Deserialize<T>(Base64Url.DecodeFromChars(crudo[0]), Web);
            return valor is not null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return false;
        }
    }
}
