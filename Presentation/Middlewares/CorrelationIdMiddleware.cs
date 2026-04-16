namespace Synergos.CMS.Presentation.Middlewares;

/// <summary>
/// Ensures every incoming request has a correlation ID — either the one
/// supplied via <c>X-Correlation-ID</c> or a freshly minted GUID. The ID
/// is echoed in the response header and pushed into the logger scope so
/// every log line during the request carries <c>{CorrelationId}</c>.
///
/// Inspired by IbeCms's logging-with-context pattern but wired via the
/// native <see cref="ILogger{T}"/> scope system — no custom logger.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";

    private readonly RequestDelegate _next;
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var fromHeader)
                            && !string.IsNullOrWhiteSpace(fromHeader)
            ? fromHeader.ToString()
            : Guid.NewGuid().ToString("N");

        context.Items[HeaderName] = correlationId;
        context.Response.OnStarting(() =>
        {
            if (!context.Response.Headers.ContainsKey(HeaderName))
                context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}
