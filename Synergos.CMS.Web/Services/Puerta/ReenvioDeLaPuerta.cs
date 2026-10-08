using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace Synergos.CMS.Web.Services.Puerta;

/// <summary>Lo que la puerta contesta: la respuesta del orquestador tal cual, o un fallo propio.</summary>
/// <param name="Estado">El estado HTTP.</param>
/// <param name="TipoDeContenido">El <c>Content-Type</c> que trajo el orquestador.</param>
/// <param name="Cuerpo">Los bytes, sin tocar.</param>
/// <param name="Fallo">Si no hay respuesta que pasar, el fallo de la puerta.</param>
public sealed record SalidaDeLaPuerta(int Estado, string? TipoDeContenido, byte[] Cuerpo, FalloDeLaPuerta? Fallo)
{
    /// <summary>Un fallo de la puerta.</summary>
    public static SalidaDeLaPuerta De(FalloDeLaPuerta fallo) => new(fallo.Estado, null, [], fallo);
}

/// <summary>Los orquestadores a los que la puerta sabe llegar: los que el despliegue dice dónde viven.</summary>
/// <remarks>
/// Por convención, <c>Synergos.Bff.X</c> vive en <c>Synergos:X:BaseUrl</c>. Sin esa clave no hay destino, y
/// un flujo de ese orquestador da 503 <c>puerta.flujo_no_disponible</c> en vez de salir a buscar un puerto
/// local que en un despliegue no existe.
/// </remarks>
public sealed class DestinosDeLaPuerta
{
    private readonly HashSet<string> _orquestadores;

    public DestinosDeLaPuerta(IEnumerable<string> orquestadores)
        => _orquestadores = new HashSet<string>(orquestadores, StringComparer.Ordinal);

    /// <summary>Si la puerta sabe llegar a <paramref name="orquestador"/>.</summary>
    public bool Tiene(string orquestador) => _orquestadores.Contains(orquestador);
}

/// <summary>
/// Manda al orquestador lo que pidió el navegador más lo que pone la puerta, y decide qué se contesta
/// (ADR 0140 F3).
/// </summary>
/// <remarks>
/// <para><b>La petición se arma de cero.</b> Método, ruta, consulta, cuerpo, el <c>Content-Type</c> de un
/// JSON y las cabeceras que pone la puerta: nada más. Del navegador no se copia ninguna cabecera —ni
/// un <c>X-Synergos-Sujeto</c> que intente nombrar a otro, ni sus cookies, ni su <c>Authorization</c>—.
/// La llave compartida y la correlación las pone la pieza del árbol, como a todo cliente del CMS.</para>
///
/// <para><b>Qué se contesta</b>, y por qué cada caso:</para>
/// <list type="bullet">
///   <item>Un 2xx y un rechazo del orquestador —un problem+json con su <c>code</c>— pasan con sus
///   bytes: la puerta no reinterpreta, y traducir para la persona es del front (ADR 0136). Sin
///   <c>Location</c>: es una ruta interna del orquestador, que el navegador no puede pedir.</item>
///   <item>Un 401 del orquestador es <b>502 <c>puerta.llave_rechazada</c></b>: es la llave compartida
///   entre los dos procesos la que falló, y pasado tal cual el front lo leería como «iniciá sesión».</item>
///   <item>Un 5xx sin rechazo, o una respuesta sin cuerpo que leer, es <b>502
///   <c>puerta.respuesta_invalida</c></b>.</item>
///   <item>Sin red: <b>503 <c>puerta.orquestador_no_disponible</c></b>; sin respuesta a tiempo: <b>504
///   <c>puerta.tiempo_agotado</c></b>. Las dos transitorias: reintentar puede salir distinto, y la llave
///   reemitida hace que el reintento no abra una segunda saga.</item>
/// </list>
///
/// <para><b>El techo es de veinticinco segundos</b> (<see cref="Techo"/>), menos que los treinta del
/// <c>TimeoutMiddleware</c>: así el 504 lo escribe la puerta, con su código, y no el corte genérico.</para>
/// </remarks>
public sealed class ReenvioDeLaPuerta
{
    /// <summary>Cuánto se espera al orquestador, reintentos incluidos.</summary>
    public static readonly TimeSpan Techo = TimeSpan.FromSeconds(25);

    /// <summary>El prefijo del cliente nombrado hacia cada orquestador.</summary>
    public const string PrefijoDelCliente = "puerta-";

    private static readonly JsonSerializerOptions Lectura = new() { PropertyNameCaseInsensitive = true };

    private readonly IHttpClientFactory _clientes;
    private readonly ILogger<ReenvioDeLaPuerta> _log;

    public ReenvioDeLaPuerta(IHttpClientFactory clientes, ILogger<ReenvioDeLaPuerta> log)
    {
        _clientes = clientes;
        _log = log;
    }

    /// <summary>El cliente nombrado hacia un orquestador: lo registra el composer con la pieza del árbol.</summary>
    public static string Cliente(string orquestador) => PrefijoDelCliente + orquestador.ToLowerInvariant();

    /// <summary>Manda la operación y decide qué se contesta.</summary>
    /// <param name="op">La operación, de la tabla.</param>
    /// <param name="parametros">Los parámetros declarados, ya leídos de la consulta.</param>
    /// <param name="cuerpo">El cuerpo del navegador, byte a byte, o nulo si la operación no lleva.</param>
    /// <param name="cabeceras">Lo que pone la puerta: sujeto, negocio, contacto y la llave reemitida.</param>
    /// <param name="ct">La cancelación de la petición del navegador.</param>
    public async Task<SalidaDeLaPuerta> EnviarAsync(
        OperacionDeLaPuerta op, IReadOnlyDictionary<string, string> parametros, byte[]? cuerpo,
        IReadOnlyDictionary<string, string> cabeceras, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(op);
        using var peticion = new HttpRequestMessage(new HttpMethod(op.Metodo), Ruta(op, parametros));
        if (op.ConCuerpo && cuerpo is not null)
        {
            peticion.Content = new ByteArrayContent(cuerpo);
            peticion.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        foreach (var (nombre, valor) in cabeceras) peticion.Headers.TryAddWithoutValidation(nombre, valor);

        HttpResponseMessage respuesta;
        try
        {
            respuesta = await _clientes.CreateClient(Cliente(op.Orquestador)).SendAsync(peticion, ct).ConfigureAwait(false);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            _log.LogWarning("La puerta no recibió respuesta de {Orquestador} a {Flujo}/{Operacion} a tiempo.",
                op.Orquestador, op.Flujo, op.Operacion);
            return SalidaDeLaPuerta.De(new FalloDeLaPuerta(StatusCodes.Status504GatewayTimeout, "puerta.tiempo_agotado",
                "El orquestador no respondió a tiempo. Reintentar con la misma llave no duplica nada.", Transitorio: true));
        }
        catch (HttpRequestException ex)
        {
            _log.LogWarning("La puerta no pudo hablar con {Orquestador} ({Flujo}/{Operacion}): {Motivo}",
                op.Orquestador, op.Flujo, op.Operacion, ex.Message);
            return SalidaDeLaPuerta.De(new FalloDeLaPuerta(StatusCodes.Status503ServiceUnavailable, "puerta.orquestador_no_disponible",
                "El orquestador no está disponible. Reintentar con la misma llave no duplica nada.", Transitorio: true));
        }

        using (respuesta)
        {
            var bytes = await respuesta.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            var tipo = respuesta.Content.Headers.ContentType?.ToString();

            if (respuesta.IsSuccessStatusCode) return new SalidaDeLaPuerta((int)respuesta.StatusCode, tipo, bytes, null);

            if (respuesta.StatusCode == HttpStatusCode.Unauthorized)
            {
                _log.LogError("{Orquestador} rechazó la llave compartida de la puerta: revisar Synergos:{Orquestador}:ApiKey.",
                    op.Orquestador, op.Orquestador);
                return SalidaDeLaPuerta.De(new FalloDeLaPuerta(StatusCodes.Status502BadGateway, "puerta.llave_rechazada",
                    "El orquestador no aceptó la llave compartida del CMS."));
            }

            // Un rechazo del orquestador trae su código: pasa tal cual, sea el estado que sea.
            if (await RechazoDelArbolDeServicios.LeerAsync(respuesta, Lectura, ct).ConfigureAwait(false) is { Codigo: { Length: > 0 } })
            {
                return new SalidaDeLaPuerta((int)respuesta.StatusCode, tipo, bytes, null);
            }

            _log.LogWarning("{Orquestador} contestó {Estado} sin un rechazo que leer a {Flujo}/{Operacion}.",
                op.Orquestador, (int)respuesta.StatusCode, op.Flujo, op.Operacion);
            return SalidaDeLaPuerta.De(new FalloDeLaPuerta(StatusCodes.Status502BadGateway, "puerta.respuesta_invalida",
                $"El orquestador contestó {(int)respuesta.StatusCode} sin un rechazo que leer."));
        }
    }

    /// <summary>La ruta del orquestador con sus parámetros puestos, y su consulta con los demás.</summary>
    private static string Ruta(OperacionDeLaPuerta op, IReadOnlyDictionary<string, string> parametros)
    {
        var ruta = op.Ruta;
        var consulta = new List<string>();
        foreach (var p in op.Parametros)
        {
            if (!parametros.TryGetValue(p.Nombre, out var valor)) continue;
            if (p.EnLaRuta) ruta = ruta.Replace("{" + p.Nombre + "}", Uri.EscapeDataString(valor), StringComparison.Ordinal);
            else consulta.Add($"{Uri.EscapeDataString(p.Nombre)}={Uri.EscapeDataString(valor)}");
        }
        return consulta.Count == 0 ? ruta : ruta + "?" + string.Join('&', consulta);
    }
}
