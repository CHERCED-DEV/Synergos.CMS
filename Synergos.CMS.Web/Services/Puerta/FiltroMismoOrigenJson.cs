using Microsoft.Net.Http.Headers;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>Un «no» de la puerta, con su estado, su código y si volver a intentarlo puede salir distinto.</summary>
/// <param name="Estado">El estado HTTP.</param>
/// <param name="Codigo">El código, siempre <c>puerta.*</c>: es lo que el front compara y traduce (ADR 0136).</param>
/// <param name="Mensaje">Para quien lee el log o la consola, no para la persona.</param>
/// <param name="Transitorio">Si reintentar puede salir distinto: una red caída sí, una sesión que falta no.</param>
public sealed record FalloDeLaPuerta(int Estado, string Codigo, string Mensaje, bool Transitorio = false);

/// <summary>
/// La defensa contra peticiones de otro sitio (CSRF) de la puerta y de lo que se compra con ella: mismo
/// origen en todo POST, JSON y con techo de tamaño cuando hay cuerpo (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>Sin antiforgery, y por qué alcanza.</b> El CMS no abre CORS, así que un POST con
/// <c>application/json</c> desde otro sitio obliga al navegador a un preflight que nadie contesta; y
/// todo POST tiene que venir del mismo origen —<c>Sec-Fetch-Site: same-origin</c>, o un <c>Origin</c>
/// del mismo host cuando el navegador no manda la primera—. Un token habría que entregárselo al custom
/// element y a páginas que salen de caché, y no agrega nada a estas dos reglas.</para>
///
/// <para><b>El host se compara sin el esquema</b>: detrás de un proxy que termina TLS, la petición llega
/// en <c>http</c> y el navegador escribió <c>https</c>. Lo que distingue a otro sitio es el host.</para>
/// </remarks>
public static class FiltroMismoOrigenJson
{
    /// <summary>El cuerpo más grande que se acepta.</summary>
    public const int CuerpoMaximo = 64 * 1024;

    /// <summary>
    /// Si un POST viene del mismo origen. Nulo si sí; el 403 si no, también cuando no hay cómo saberlo.
    /// </summary>
    public static FalloDeLaPuerta? Origen(HttpRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (!HttpMethods.IsPost(req.Method)) return null;

        var sitio = req.Headers["Sec-Fetch-Site"].ToString();
        if (!string.IsNullOrEmpty(sitio))
        {
            return string.Equals(sitio, "same-origin", StringComparison.OrdinalIgnoreCase)
                ? null
                : Ajeno($"Sec-Fetch-Site es «{sitio}»: sólo se acepta same-origin.");
        }

        var origen = req.Headers.Origin.ToString();
        if (Uri.TryCreate(origen, UriKind.Absolute, out var uri)
            && string.Equals(uri.Authority, req.Host.Value, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Ajeno(string.IsNullOrEmpty(origen)
            ? "Sin Sec-Fetch-Site ni Origin no se sabe de dónde viene: se rechaza."
            : "El Origin no es el de este sitio.");
    }

    /// <summary>Si el cuerpo es JSON. Nulo si sí; el 415 si no.</summary>
    public static FalloDeLaPuerta? Tipo(HttpRequest req)
    {
        ArgumentNullException.ThrowIfNull(req);
        return MediaTypeHeaderValue.TryParse(req.ContentType, out var tipo)
               && string.Equals(tipo.MediaType.Value, "application/json", StringComparison.OrdinalIgnoreCase)
            ? null
            : new FalloDeLaPuerta(StatusCodes.Status415UnsupportedMediaType, "puerta.tipo_no_soportado",
                "El cuerpo tiene que ser application/json.");
    }

    /// <summary>Lee el cuerpo entero, con el techo de <see cref="CuerpoMaximo"/>: el cuerpo, o el 413.</summary>
    /// <remarks>
    /// Se mira el <c>Content-Length</c> y TAMBIÉN lo que de verdad llega: un cuerpo por trozos no lo
    /// declara, y sin contar se leería entero en memoria.
    /// </remarks>
    public static async Task<(byte[]? Cuerpo, FalloDeLaPuerta? Fallo)> LeerCuerpoAsync(HttpRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        if (req.ContentLength > CuerpoMaximo) return (null, Grande());

        using var memoria = new MemoryStream();
        var bufer = new byte[8192];
        int leidos;
        while ((leidos = await req.Body.ReadAsync(bufer, ct).ConfigureAwait(false)) > 0)
        {
            if (memoria.Length + leidos > CuerpoMaximo) return (null, Grande());
            memoria.Write(bufer, 0, leidos);
        }
        return (memoria.ToArray(), null);
    }

    private static FalloDeLaPuerta Ajeno(string mensaje)
        => new(StatusCodes.Status403Forbidden, "puerta.origen_no_permitido", mensaje);

    private static FalloDeLaPuerta Grande()
        => new(StatusCodes.Status413PayloadTooLarge, "puerta.cuerpo_demasiado_grande",
            $"El cuerpo pasa de {CuerpoMaximo / 1024} KB.");
}
