using System.Diagnostics;

namespace Synergos.CMS.Web.Middlewares;

/// <summary>
/// Assigns a correlation ID to every incoming request and echoes it on
/// the response via the <c>X-Correlation-Id</c> header so clients and
/// log aggregators can trace a single request end-to-end.
/// </summary>
/// <remarks>
/// Resolution order for the id value:
/// <list type="number">
///   <item>Incoming <c>X-Correlation-Id</c> header, when non-empty.</item>
///   <item>Current <see cref="Activity"/> trace id (when an OpenTelemetry
///     activity is present).</item>
///   <item>A freshly generated <see cref="Guid"/>.</item>
/// </list>
/// Downstream code can read the value from <c>HttpContext.Items["CorrelationId"]</c>.
/// The response header is set via <c>HttpContext.Response.OnStarting</c>
/// so it survives even on short-circuited or error responses.
/// <para><b>Lo que llega de fuera se limpia con la regla de <c>Synergos.Shared.Correlation</c></b> (ADR
/// 0140 F3): letras y dígitos ASCII, hasta 32. Antes se copiaba tal cual —mil caracteres, saltos de línea
/// que parten el registro— y al cruzar a un orquestador, que sí la limpia, el CMS y el árbol de servicios
/// registraban la misma petición con dos identificadores distintos. Con la puerta, el navegador la manda
/// directo; la regla se comparte por su resultado y no por su código (el CMS no referencia Shared), y hay
/// gate que las cruza (<c>CorrelacionUnaSolaTests</c>).</para>
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ContextKey = "CorrelationId";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);
        context.Items[ContextKey] = correlationId;

        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
            {
                context.Response.Headers[HeaderName] = correlationId;
            }
            return Task.CompletedTask;
        });

        return _next(context);
    }

    internal static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && Normalizar(incoming.ToString()) is { } limpio)
        {
            return limpio;
        }

        var activityId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(activityId)
            ? Guid.NewGuid().ToString("N")
            : activityId;
    }

    /// <summary>
    /// La correlación de fuera, limpia: sus letras y dígitos ASCII, hasta 32; nula si no queda nada.
    /// </summary>
    internal static string? Normalizar(string? entrante)
    {
        var limpio = new string((entrante ?? string.Empty).Where(char.IsAsciiLetterOrDigit).Take(32).ToArray());
        return limpio.Length == 0 ? null : limpio;
    }
}
