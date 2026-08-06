using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Siembra al boot los hilos de DM y los ítems guardados de la demo de Blogs
/// (OLA 6 / doc 21 §2.3) sobre los seams GENÉRICOS ya registrados
/// (<see cref="IMessagingService"/> contexto <c>dm</c>, <see cref="IUserCollection"/>
/// colección <c>saved</c>), delegando la lógica pura a
/// <see cref="BlogsDemoSeeder"/> (Application). Se hace desde un hosted service —
/// NO en el ctor de los stubs — porque esos singletons son compartidos por varios
/// dominios (Tienda post-venta usa el mismo <see cref="IMessagingService"/>): la
/// data de Blogs no debe vivir en la semilla de la mensajería genérica.
/// </summary>
/// <remarks>
/// Idempotente (ver <see cref="BlogsDemoSeeder"/>): re-ejecutar no duplica. No es
/// un "seeder de contenido" prohibido por ADR 0013 (que mira schema/DB de
/// Umbraco): solo hidrata stubs in-memory de demo, igual que
/// <see cref="StubSocialGraphService"/> siembra el grafo en su ctor.
/// </remarks>
public sealed class BlogsDemoSeedHostedService : IHostedService
{
    private readonly IMessagingService _messaging;
    private readonly IUserCollection _collections;
    private readonly ILogger<BlogsDemoSeedHostedService> _logger;

    public BlogsDemoSeedHostedService(
        IMessagingService messaging,
        IUserCollection collections,
        ILogger<BlogsDemoSeedHostedService> logger)
    {
        _messaging = messaging;
        _collections = collections;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await BlogsDemoSeeder.SeedAsync(_messaging, _collections, cancellationToken);
            _logger.LogInformation(
                "Blogs demo seed OK: {Threads} hilos DM + guardados de {Owners} actores.",
                BlogsDemoSeeder.DmThreadCount, BlogsDemoSeeder.SavedOwnerCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Blogs demo seed falló; la demo de DMs/guardados puede salir vacía.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
