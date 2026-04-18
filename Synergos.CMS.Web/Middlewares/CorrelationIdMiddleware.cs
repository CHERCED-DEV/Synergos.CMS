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
            && !string.IsNullOrWhiteSpace(incoming))
        {
            return incoming.ToString();
        }

        var activityId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrWhiteSpace(activityId)
            ? Guid.NewGuid().ToString("N")
            : activityId;
    }
}
