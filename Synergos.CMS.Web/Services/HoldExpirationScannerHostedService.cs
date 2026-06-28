using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Background <see cref="IHostedService"/> que cada <see cref="ScanInterval"/>
/// invoca <see cref="IReservationService.ExpireStaleHoldsAsync"/> para liberar
/// los holds vencidos del motor de reservas: un cupo apartado (<c>Held</c>) que
/// no se confirma dentro de la ventana de hold se transiciona a <c>Expired</c> y
/// el cupo queda libre. Aprendizaje de NS.Booking (doc 17): sin esto, un abandono
/// en checkout dejaría el cupo bloqueado indefinidamente.
/// </summary>
/// <remarks>
/// El motor ya rechaza confirmar un hold vencido in-line (<see cref="StubReservationService"/>);
/// este scanner es el barrido proactivo que libera el inventario aunque el huésped
/// nunca vuelva. Intervalo fijo (no settings) — el adapter real (PMS/DB) lo
/// reemplazaría por el TTL nativo del sistema de inventario.
/// </remarks>
public sealed class HoldExpirationScannerHostedService : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(2);

    private readonly IReservationService _reservations;
    private readonly ILogger<HoldExpirationScannerHostedService> _logger;

    public HoldExpirationScannerHostedService(
        IReservationService reservations,
        ILogger<HoldExpirationScannerHostedService> logger)
    {
        _reservations = reservations;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Delay inicial para dejar terminar el boot antes del primer scan.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var expired = await _reservations.ExpireStaleHoldsAsync(stoppingToken);
                if (expired > 0)
                {
                    _logger.LogInformation(
                        "Hold expiration scan: {Count} reserva(s) liberada(s) por hold vencido", expired);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Hold expiration scan falló — siguiente intento en {Interval}", ScanInterval);
            }

            try
            {
                await Task.Delay(ScanInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
