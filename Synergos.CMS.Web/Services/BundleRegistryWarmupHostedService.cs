using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Fuerza la construcción del <see cref="IBundleRegistryClient"/>
/// singleton al boot del host, para que el ctor del adapter
/// (FileSystemBundleRegistryClient) corra inmediatamente y emita sus
/// logs (registry loaded, hot-reload watcher attached, etc.) sin
/// esperar al primer SynHost render. Cap-280 Batch B (Ola 285).
/// </summary>
/// <remarks>
/// Sin este warmup, el cliente queda lazy — se instancia solo cuando
/// algún caller lo pide. Si el sitio recién boot no tiene content
/// publicado, nadie invoca al SynHostEmitter y el operador no tiene
/// visibility del estado del registry hasta el primer render real.
///
/// El service también funciona como diagnostic: log explícito del
/// adapter type que el composer registró (Stub | FileSystem | Http).
/// </remarks>
public sealed class BundleRegistryWarmupHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<BundleRegistryWarmupHostedService> _logger;

    public BundleRegistryWarmupHostedService(
        IServiceProvider serviceProvider,
        ILogger<BundleRegistryWarmupHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = _serviceProvider.GetRequiredService<IBundleRegistryClient>();
            _logger.LogInformation(
                "BundleRegistry warmup OK: adapter={AdapterType}", client.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BundleRegistry warmup failed; subsequent SynHost renders may also fail");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
