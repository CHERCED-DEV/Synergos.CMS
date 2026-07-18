using System.Text;
using Microsoft.AspNetCore.Mvc;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Web.Controllers;

/// <summary>
/// Flujo EN VIVO (T7) — Server-Sent Events. Un solo endpoint transversal: el cliente
/// abre <c>GET /api/realtime/stream?channel=…</c> y recibe los hechos del canal.
/// </summary>
/// <remarks>
/// <para><b>Por qué SSE y no SignalR</b> (el rótulo del doc 25): las tres superficies que
/// el doc nombra —DMs, feed, check-in— son <b>server → client</b>. Enviar ya es un POST
/// que existe; lo que faltaba era que el servidor avisara. SSE cubre eso con <b>cero
/// dependencias nuevas</b> (el navegador trae <c>EventSource</c>, con reconexión
/// automática), mientras que el cliente JS de SignalR sería un paquete npm más en cada
/// bundle servido por CDN. Ver ADR 0111 para el criterio de reapertura.</para>
/// <para><b>La autorización por canal es la razón de ser de este controller.</b> Las tres
/// olas anteriores cerraron el acceso a expedientes, entradas y consolas; un flujo en
/// vivo sin política sería la puerta trasera a todo eso. Por eso el mapeo canal→permiso
/// es EXPLÍCITO y <b>fail-closed</b>: un canal que nadie declaró NO se puede leer.</para>
/// </remarks>
[ApiController]
[Route("api/realtime")]
public sealed class RealtimeController : ControllerBase
{
    /// <summary>
    /// Cada cuánto se manda un comentario de keep-alive. Sin esto, proxies y balanceadores
    /// cortan una conexión "inactiva" y el cliente reconecta en bucle.
    /// </summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(20);

    /// <summary>Prefijo del canal de check-in de un evento: <c>eventos:checkin:{eventId}</c>.</summary>
    public const string EventosCheckinPrefix = "eventos:checkin:";

    private readonly SseRealtimeHub _hub;
    private readonly IMemberAccessGate _gate;
    private readonly ILogger<RealtimeController> _logger;

    public RealtimeController(SseRealtimeHub hub, IMemberAccessGate gate, ILogger<RealtimeController> logger)
    {
        _hub = hub;
        _gate = gate;
        _logger = logger;
    }

    /// <summary>
    /// ¿Puede el usuario actual LEER este canal? <c>null</c> = sí; si no, el resultado a
    /// devolver.
    /// </summary>
    /// <remarks>
    /// <b>Fail-closed a propósito:</b> el <c>default</c> deniega. Añadir un canal nuevo
    /// obliga a declarar aquí quién lo lee; olvidarlo lo deja cerrado, no abierto — que
    /// es el único default aceptable para un flujo que empuja datos de negocio.
    /// </remarks>
    /// <returns>
    /// El código HTTP con el que denegar, o <c>null</c> si puede leer. Se devuelve el
    /// código y no un <c>IActionResult</c> porque este endpoint escribe la respuesta a
    /// mano (es un stream, no una acción que devuelva un objeto) — mismo patrón que el
    /// export en streaming de <c>AdminController</c>.
    /// </returns>
    private int? DeniedStatusFor(string channel)
    {
        if (channel.StartsWith(EventosCheckinPrefix, StringComparison.Ordinal))
        {
            // El feed de check-in dice quién está entrando al evento: mismo permiso que
            // la consola del organizador que lo muestra (T2-Eventos).
            if (!_gate.IsAuthenticated)
            {
                return StatusCodes.Status401Unauthorized;
            }
            if (!_gate.HasAnyRole("organizador,admin"))
            {
                return StatusCodes.Status403Forbidden;
            }
            return null;
        }

        // Canal desconocido: NO se abre. Añadir uno exige declarar su política arriba.
        _logger.LogWarning("Realtime: canal sin política declarada, denegado: {Channel}", channel);
        return StatusCodes.Status403Forbidden;
    }

    // GET /api/realtime/stream?channel=eventos:checkin:{eventId}
    [HttpGet("stream")]
    public async Task Stream([FromQuery] string? channel, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(channel))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // Autorizar ANTES de abrir el flujo: un 401/403 debe ser una respuesta normal y
        // corta, no un stream que luego se corta a medias.
        if (DeniedStatusFor(channel.Trim()) is { } denied)
        {
            Response.StatusCode = denied;
            return;
        }

        Response.ContentType = "text/event-stream";
        // Sin buffering intermedio: si un proxy acumula, el "vivo" deja de serlo.
        Response.Headers["Cache-Control"] = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";

        var reader = _hub.Subscribe(channel.Trim(), out var unsubscribe);
        try
        {
            // Un primer evento inmediato confirma al cliente que el canal está abierto
            // (si no, no distingue "conectado y sin novedades" de "colgado").
            await WriteEventAsync("ready", "{}", cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                RealtimeMessage message;
                try
                {
                    // Espera con tope: al vencer se manda un latido y se vuelve a esperar.
                    using var heartbeat = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    heartbeat.CancelAfter(HeartbeatInterval);
                    message = await reader.ReadAsync(heartbeat.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Venció el latido, no el cliente: mandar el comentario y seguir.
                    await Response.WriteAsync(": keep-alive\n\n", cancellationToken);
                    await Response.Body.FlushAsync(cancellationToken);
                    continue;
                }

                await WriteEventAsync(message.EventName, message.PayloadJson, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // El cliente cerró la pestaña. Es el final normal de un stream, no un error.
        }
        finally
        {
            unsubscribe();
        }
    }

    /// <summary>
    /// Escribe un evento en el formato de la especificación SSE:
    /// <c>event: {nombre}\ndata: {json}\n\n</c>.
    /// </summary>
    /// <remarks>
    /// El payload se manda en UNA línea: un salto de línea dentro de <c>data:</c> partiría
    /// el evento en dos y el cliente recibiría JSON truncado. Como es JSON serializado,
    /// los saltos ya vienen escapados; se sanea igual por defensa en profundidad.
    /// </remarks>
    private async Task WriteEventAsync(string eventName, string payloadJson, CancellationToken cancellationToken)
    {
        var oneLine = payloadJson.Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);

        var frame = new StringBuilder()
            .Append("event: ").Append(eventName).Append('\n')
            .Append("data: ").Append(oneLine).Append("\n\n")
            .ToString();

        await Response.WriteAsync(frame, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
