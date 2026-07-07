using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Siembra al boot la correspondencia de demo del vertical Gobierno (OLA 8) sobre el
/// seam GENÉRICO <see cref="IMessagingService"/> (contexto <c>gov</c>), delegando la
/// lógica pura a <see cref="GovCorrespondenceSeeder"/> (Application). Se hace desde un
/// hosted service — NO en el ctor del stub de mensajería — porque ese singleton es
/// compartido por varios dominios (Tienda/Blogs/Healthcare usan el mismo
/// <see cref="IMessagingService"/>): la data de Gobierno no debe vivir en la semilla de
/// la mensajería genérica (mismo patrón que <see cref="BlogsDemoSeedHostedService"/>).
/// </summary>
public sealed class GovCorrespondenceSeedHostedService : IHostedService
{
    private readonly IMessagingService _messaging;
    private readonly ILogger<GovCorrespondenceSeedHostedService> _logger;

    public GovCorrespondenceSeedHostedService(
        IMessagingService messaging,
        ILogger<GovCorrespondenceSeedHostedService> logger)
    {
        _messaging = messaging;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await GovCorrespondenceSeeder.SeedAsync(_messaging, cancellationToken);
            _logger.LogInformation(
                "Gobierno demo seed OK: {Threads} hilos de correspondencia (contexto gov).",
                GovCorrespondenceSeeder.ThreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gobierno demo seed falló; la correspondencia del expediente puede salir vacía.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
