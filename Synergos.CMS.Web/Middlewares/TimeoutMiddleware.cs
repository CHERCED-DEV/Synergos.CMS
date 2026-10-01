using Microsoft.AspNetCore.Http.Timeouts;

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
/// <para><b>Un plazo vencido NO sale como 200</b> (#188). El plazo sólo corta lo que
/// observa el token, y el resto sigue: el sembrador tarda dos minutos en síncrono, termina
/// bien, y al escribir el JSON el formateador encuentra <c>RequestAborted</c> cancelado,
/// cree que el cliente se fue y no escribe nada —sin lanzar—. La excepción que este
/// middleware esperaba no llegaba nunca, y la respuesta era <c>200</c> con el cuerpo
/// vacío: «salió bien» sin decir qué. Por eso también se mira al VOLVER: si el plazo
/// venció y la respuesta no empezó, es el 504 que este contrato promete.</para>
/// <para><b>Lo que de verdad tarda se declara</b>: el middleware publica
/// <see cref="IHttpRequestTimeoutFeature"/> —el rasgo estándar de ASP.NET Core— y quien
/// sabe que su trabajo excede el plazo lo apaga (<c>[SinPlazoDePeticion]</c>). Sin eso, el
/// 504 dice la verdad sobre el plazo pero se calla lo que el trabajo sí hizo.</para>
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
        var rasgoAnterior = context.Features.Get<IHttpRequestTimeoutFeature>();
        context.Features.Set<IHttpRequestTimeoutFeature>(new PlazoDeLaPeticion(linkedCts));

        try
        {
            await _next(context).ConfigureAwait(false);

            if (linkedCts.IsCancellationRequested
                && !originalToken.IsCancellationRequested
                && !context.Response.HasStarted)
            {
                context.Response.Clear();
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
            }
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
            context.Features.Set(rasgoAnterior);
        }
    }

    /// <summary>El plazo de esta petición, para quien necesite apagarlo.</summary>
    private sealed class PlazoDeLaPeticion : IHttpRequestTimeoutFeature
    {
        private readonly CancellationTokenSource _plazo;

        public PlazoDeLaPeticion(CancellationTokenSource plazo) => _plazo = plazo;

        public CancellationToken RequestTimeoutToken => _plazo.Token;

        public void DisableTimeout() => _plazo.CancelAfter(Timeout.InfiniteTimeSpan);
    }
}
