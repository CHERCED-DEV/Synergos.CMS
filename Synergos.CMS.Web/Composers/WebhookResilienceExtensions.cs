using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

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
        var channelName = builder.Name;

        // Olas 165-166 — telemetry handler closest to user code (added
        // ANTES del resilience handler). Mide elapsed total incluyendo
        // retries internas del resilience pipeline.
        builder.AddHttpMessageHandler(sp =>
            new WebhookTelemetryHandler(
                channelName,
                sp.GetRequiredService<IWebhookTelemetryStore>()));

        var pipeline = builder.AddStandardResilienceHandler();
        pipeline.Services
            .AddOptions<HttpStandardResilienceOptions>(pipeline.PipelineName)
            .Configure<IOptionsMonitor<WebhookResilienceSettings>>((opts, monitor) =>
            {
                var s = monitor.CurrentValue;
                // Olas 157-158 — per-channel override merges sobre los defaults
                // top-level. Solo campos no-null en el override reemplazan.
                s.PerChannel.TryGetValue(channelName, out var ovr);
                var maxRetries = ovr?.MaxRetryAttempts ?? s.MaxRetryAttempts;
                var retryDelayMs = ovr?.RetryBaseDelayMs ?? s.RetryBaseDelayMs;
                var attemptTimeoutSec = ovr?.AttemptTimeoutSeconds ?? s.AttemptTimeoutSeconds;
                var totalTimeoutSec = ovr?.TotalRequestTimeoutSeconds ?? s.TotalRequestTimeoutSeconds;

                opts.Retry.MaxRetryAttempts = maxRetries;
                opts.Retry.Delay = TimeSpan.FromMilliseconds(retryDelayMs);
                opts.AttemptTimeout.Timeout = TimeSpan.FromSeconds(attemptTimeoutSec);
                opts.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(totalTimeoutSec);
            });
        return builder;
    }
}
