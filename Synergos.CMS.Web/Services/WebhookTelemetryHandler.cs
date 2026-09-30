using System.Diagnostics;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// <see cref="DelegatingHandler"/> que mide latencia + outcome de
/// cada llamada saliente de un canal y los registra en
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
///
/// <para><b>Mide también los clientes del árbol de servicios</b> (#178), y para ellos «éxito» NO
/// es un 2xx: un 404 o un 409 de una capacidad es una respuesta —«no existe», «ya estaba»—,
/// no una caída del canal. Por eso quien lo enchufa puede decir qué cuenta como éxito; sin
/// decirlo, sigue siendo el 2xx de siempre, que es lo correcto para un webhook a un
/// tercero.</para>
/// </remarks>
public sealed class WebhookTelemetryHandler : DelegatingHandler
{
    private readonly string _channelName;
    private readonly IWebhookTelemetryStore _telemetryStore;
    private readonly Func<HttpResponseMessage, bool> _esExito;

    public WebhookTelemetryHandler(
        string channelName,
        IWebhookTelemetryStore telemetryStore,
        Func<HttpResponseMessage, bool>? esExito = null)
    {
        _channelName = channelName;
        _telemetryStore = telemetryStore;
        _esExito = esExito ?? (r => r.IsSuccessStatusCode);
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
                _esExito(response));
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
