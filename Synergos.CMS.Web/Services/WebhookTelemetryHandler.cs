using System.Diagnostics;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="DelegatingHandler"/> que mide latencia + outcome de
/// cada outgoing webhook y los registra en
/// <see cref="IWebhookTelemetryStore"/>. El channelName se inyecta
/// vía constructor por el HttpClientFactory cuando se wirea con
/// <c>AddHttpMessageHandler</c>.
/// </summary>
/// <remarks>
/// Si se registra como primer handler en el pipeline (closest to
/// user code), captura latencia total INCLUYENDO retries del
/// resilience handler downstream — útil para SLO tracking.
///
/// Una excepción downstream se cuenta como failure con statusCode 0.
///
/// Olas 165-166.
/// </remarks>
public sealed class WebhookTelemetryHandler : DelegatingHandler
{
    private readonly string _channelName;
    private readonly IWebhookTelemetryStore _telemetryStore;

    public WebhookTelemetryHandler(string channelName, IWebhookTelemetryStore telemetryStore)
    {
        _channelName = channelName;
        _telemetryStore = telemetryStore;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            sw.Stop();
            _telemetryStore.RecordOutcome(
                _channelName,
                sw.Elapsed,
                (int)response.StatusCode,
                response.IsSuccessStatusCode);
            return response;
        }
        catch
        {
            sw.Stop();
            _telemetryStore.RecordOutcome(_channelName, sw.Elapsed, 0, false);
            throw;
        }
    }
}
