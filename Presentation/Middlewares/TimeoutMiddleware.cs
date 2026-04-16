using System.Diagnostics;

namespace Synergos.CMS.Presentation.Middlewares;

/// <summary>
/// Aborts requests that exceed a configurable timeout, returning HTTP 408
/// instead of leaking thread-pool capacity to hung downstream calls. Backoffice
/// and plugin paths are excluded — long-running editor operations (publish,
/// uSync export, indexing) must not be killed by this middleware.
///
/// Inspired by NS.Booking.CMS/IbeCms — production-resilience guard that
/// converts indefinite stalls into observable 408s with elapsed-time logging.
///
/// Configuration in appsettings.json:
/// <code>
/// "Synergos": {
///   "Runtime": {
///     "RequestTimeoutSeconds": 90,
///     "TimeoutExcludedPaths": [ "/umbraco/", "/App_Plugins/", "/media/" ]
///   }
/// }
/// </code>
/// </summary>
public sealed class TimeoutMiddleware
{
    private readonly RequestDelegate _next;
    private readonly TimeSpan        _timeout;
    private readonly string[]        _excludedPaths;
    private readonly ILogger<TimeoutMiddleware> _logger;

    public TimeoutMiddleware(
        RequestDelegate              next,
        ILogger<TimeoutMiddleware>   logger,
        TimeSpan                     timeout,
        string[]                     excludedPaths)
    {
        _next          = next;
        _logger        = logger;
        _timeout       = timeout;
        _excludedPaths = excludedPaths;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Skip excluded paths — backoffice + plugin routes must not be aborted.
        var path = context.Request.Path.Value ?? string.Empty;
        if (_excludedPaths.Any(prefix => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        using var timeoutCts = new CancellationTokenSource(_timeout);
        using var linked     = CancellationTokenSource.CreateLinkedTokenSource(
            context.RequestAborted, timeoutCts.Token);

        var pipelineTask = Task.Run(() => _next(context), linked.Token);
        var timeoutTask  = Task.Delay(Timeout.Infinite, linked.Token);

        var winner = await Task.WhenAny(pipelineTask, timeoutTask);

        if (winner == timeoutTask && timeoutCts.IsCancellationRequested && !context.RequestAborted.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                "TimeoutMiddleware: aborted request {Method} {Path} after {ElapsedMs} ms (configured {TimeoutMs} ms)",
                context.Request.Method, path, sw.ElapsedMilliseconds, (long)_timeout.TotalMilliseconds);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                await context.Response.WriteAsync("Request timed out.");
            }
            return;
        }

        // Pipeline finished (success or its own exception) — surface it.
        await pipelineTask;
    }
}

public static class TimeoutMiddlewareExtensions
{
    public static IApplicationBuilder UseSynergosTimeout(
        this IApplicationBuilder app,
        TimeSpan                 timeout,
        params string[]          excludedPaths)
        => app.UseMiddleware<TimeoutMiddleware>(timeout, excludedPaths);
}
