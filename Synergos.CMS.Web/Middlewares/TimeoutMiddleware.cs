namespace Synergos.CMS.Web.Middlewares;

/// <summary>
/// Enforces a maximum duration for handling the request pipeline. If
/// the timeout elapses before the downstream pipeline completes, the
/// response is aborted with HTTP 504 Gateway Timeout (when the response
/// has not yet started).
/// </summary>
/// <remarks>
/// The middleware links a fresh <see cref="CancellationTokenSource"/>
/// with <c>HttpContext.RequestAborted</c>, swaps the context's abort
/// token so downstream code naturally observes the timeout, and
/// restores the original token on exit. A genuine client-abort (original
/// token cancelled) is passed through unchanged — the timeout only
/// fires when the middleware's own linked CTS triggered the
/// cancellation.
/// Default timeout is 30 seconds; a custom value can be supplied at
/// registration time.
/// </remarks>
public sealed class TimeoutMiddleware
{
    /// <summary>Default timeout applied when none is supplied.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly RequestDelegate _next;
    private readonly TimeSpan _timeout;

    public TimeoutMiddleware(RequestDelegate next)
        : this(next, DefaultTimeout) { }

    public TimeoutMiddleware(RequestDelegate next, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout), timeout, "Timeout must be a positive duration.");
        }

        _next = next;
        _timeout = timeout;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var originalToken = context.RequestAborted;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(originalToken);
        linkedCts.CancelAfter(_timeout);

        context.RequestAborted = linkedCts.Token;

        try
        {
            await _next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (linkedCts.IsCancellationRequested && !originalToken.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
        }
        finally
        {
            context.RequestAborted = originalToken;
        }
    }
}
