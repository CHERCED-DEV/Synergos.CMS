using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Composers;

/// <summary>
/// Extension method para wireear el StandardResilienceHandler de un
/// named HttpClient con los settings hot-reloadable de
/// <see cref="WebhookResilienceSettings"/>. Olas 146-147.
/// </summary>
internal static class WebhookResilienceExtensions
{
    /// <summary>
    /// Aplica retry/timeouts/circuit-breaker basados en
    /// <see cref="WebhookResilienceSettings"/> via
    /// <see cref="IOptionsMonitor{TOptions}"/>. Cuando los settings cambian
    /// (appsettings.json reload), el siguiente HttpClient handler rotation
    /// (HttpClientFactory default 2 minutos) construye una pipeline nueva
    /// con los valores frescos — sin restart.
    /// </summary>
    public static IHttpClientBuilder AddWebhookResilience(this IHttpClientBuilder builder)
    {
        var pipeline = builder.AddStandardResilienceHandler();
        pipeline.Services
            .AddOptions<HttpStandardResilienceOptions>(pipeline.PipelineName)
            .Configure<IOptionsMonitor<WebhookResilienceSettings>>((opts, monitor) =>
            {
                var s = monitor.CurrentValue;
                opts.Retry.MaxRetryAttempts = s.MaxRetryAttempts;
                opts.Retry.Delay = TimeSpan.FromMilliseconds(s.RetryBaseDelayMs);
                opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(s.AttemptTimeoutSeconds);
                opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(s.TotalRequestTimeoutSeconds);
            });
        return builder;
    }
}
