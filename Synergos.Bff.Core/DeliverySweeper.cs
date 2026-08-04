using System.Net.Http.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Synergos.Bff.Core;

/// <summary>
/// Quien vuelve por los avisos que quedaron a medias (HU #29).
/// </summary>
/// <remarks>
/// <para><b>Por qué acá y no dentro de <c>Api.Notifications</c>.</b> La capacidad pasa el filtro
/// de atomicidad —dice NO sola y es dueña de su almacén—, así que la tentación es ponerle el
/// barrido adentro. Pero un barrido periódico <i>es</i> la máquina de reintentar y rendirse, y
/// esa máquina ya vive acá para las compensaciones. Duplicarla daría dos techos, dos cadencias y
/// dos ideas de cuándo rendirse — y el día que difieran, nadie sabría cuál manda
/// (<c>CLAUDE.md</c> §11). Hay gate.</para>
///
/// <para><b>El reparto, dicho de una vez:</b> la capacidad sabe QUÉ está colgado y CÓMO se
/// reintenta un envío; acá se decide CUÁNDO se vuelve a intentar y CUÁNTAS veces antes de
/// rendirse.</para>
///
/// <para><b>Rendirse es un estado, no un silencio.</b> Un envío que se reintenta para siempre
/// consume la cuota del proveedor y, sobre todo, <i>esconde el fallo real</i>: un <c>Queued</c>
/// eterno se lee como «va en camino». Al llegar al techo se marca <c>GivenUp</c> con la última
/// causa, para que alguien pueda mirarlo.</para>
///
/// <para><b>Este barrido corre en el host del orquestador</b>, que es quien ya tiene el cliente
/// nombrado hacia <c>Api.Notifications</c> —lo necesita para el aviso de compensación colgada—.
/// No hace falta un proceso nuevo.</para>
/// </remarks>
public sealed class DeliverySweeper : BackgroundService
{
    private readonly IHttpClientFactory _clientes;
    private readonly IOptionsMonitor<SweepOptions> _opciones;
    private readonly ILogger<DeliverySweeper> _log;

    public DeliverySweeper(
        IHttpClientFactory clientes,
        IOptionsMonitor<SweepOptions> opciones,
        ILogger<DeliverySweeper> log)
    {
        _clientes = clientes;
        _opciones = opciones;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await UnaVueltaAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Que el barrido falle no puede tumbarlo: se pierde una vuelta, no la bitácora.
                _log.LogWarning(ex, "El barrido de envíos falló; se reintenta al siguiente ciclo.");
            }

            try { await Task.Delay(Cadencia, stoppingToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private TimeSpan Cadencia => TimeSpan.FromSeconds(Math.Max(5, _opciones.CurrentValue.IntervalSeconds));

    /// <summary>Una vuelta: mira lo colgado, reintenta lo que le queda techo, rinde lo que no.</summary>
    /// <remarks>
    /// Público a propósito, y no un detalle del lazo: <b>las reglas están acá y el
    /// <see cref="BackgroundService"/> es solo el reloj</b>. Un barrido cuyas decisiones solo se
    /// puedan observar esperando a que un lazo de fondo dé una vuelta se prueba con esperas, y un
    /// test con esperas falla por lento el día que menos conviene.
    /// </remarks>
    public async Task UnaVueltaAsync(CancellationToken ct)
    {
        var techo = _opciones.CurrentValue.DeliveryRetryCeiling;
        if (techo <= 0) return;   // apagado a propósito

        var http = _clientes.CreateClient(CompensationAlert.Capability);

        var pagina = await http.GetFromJsonAsync<PaginaDto>("v1/deliveries/queued?limit=100", ct)
            .ConfigureAwait(false);
        if (pagina?.Items is null || pagina.Items.Count == 0) return;

        if (pagina.HasMore)
        {
            // Se atiende una página por vuelta y se DICE. Un tope silencioso se lee como
            // «no hay nada más», y la diferencia entre «100 colgados» y «40.000 colgados» es
            // justo la que decide si esto es un incidente. Los que no entraron son los más
            // nuevos —la lista viene del más viejo— así que la siguiente vuelta los alcanza.
            _log.LogWarning(
                "Hay {Total} avisos colgados y esta vuelta atiende {Cuantos}. El resto espera al siguiente ciclo.",
                pagina.Total, pagina.Items.Count);
        }

        foreach (var envio in pagina.Items)
        {
            // El techo se comprueba ANTES de reintentar, no después. Al revés se haría un intento
            // de más — el que sobrepasa el techo— contra un proveedor que ya dijo que no ocho
            // veces.
            if (envio.Attempts >= techo)
            {
                await RendirseAsync(http, envio, techo, ct).ConfigureAwait(false);
                continue;
            }

            using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/deliveries/{Uri.EscapeDataString(envio.Id)}/retry");
            using var res = await http.SendAsync(req, ct).ConfigureAwait(false);

            if (res.IsSuccessStatusCode)
            {
                _log.LogInformation("Envío {Id} salió en el reintento {Intento}.", envio.Id, envio.Attempts + 1);
            }
            else
            {
                // No se grita por cada fallo: el reintento fallido es el caso ESPERADO de este
                // barrido, y un warning por vuelta llenaría el log de ruido normal. Lo que sí se
                // grita es rendirse.
                _log.LogDebug("Envío {Id}: el reintento {Intento} tampoco salió ({Status}).",
                    envio.Id, envio.Attempts + 1, (int)res.StatusCode);
            }
        }
    }

    private async Task RendirseAsync(HttpClient http, EnvioDto envio, int techo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, $"v1/deliveries/{Uri.EscapeDataString(envio.Id)}/give-up")
        {
            Content = JsonContent.Create(new
            {
                reason = $"{techo} intentos sin salir. Última causa: {envio.LastError ?? "sin detalle"}",
            }),
        };

        using var res = await http.SendAsync(req, ct).ConfigureAwait(false);

        // Esto SÍ se grita: es lo único que este barrido no puede resolver solo.
        _log.LogWarning(
            "Envío {Id} ABANDONADO tras {Intentos} intentos ({Estado}). Última causa: {Causa}",
            envio.Id, envio.Attempts, res.IsSuccessStatusCode ? "anotado" : "no se pudo anotar",
            envio.LastError ?? "sin detalle");
    }

    // La forma de lo que llega. Vive acá y no en Synergos.Core: es el contrato HTTP con una
    // capacidad, no vocabulario del dominio.
    private sealed record PaginaDto(List<EnvioDto>? Items, int Total, int Offset, bool HasMore);

    private sealed record EnvioDto(string Id, string Status, int Attempts, string? LastError);
}
