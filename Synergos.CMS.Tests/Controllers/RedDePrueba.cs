using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Una red social entera para probar lo que ve cada persona (012-blogs-social, I0): los stubs de verdad, la cara pública
/// de verdad y el repositorio de comentarios de verdad sobre una carpeta temporal. Cada persona entra con su sesión
/// SIMULADA (el gate), que es como se prueba sin crear cuentas.
/// </summary>
internal sealed class RedDePrueba : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "syn-red-012-" + Guid.NewGuid().ToString("N"));
    private readonly InMemoryFormRateLimiter _limitador;

    public RedDePrueba(
        SemillaDeLaRed semilla,
        IEnumerable<(string Correo, string? Nombre)> miembros,
        bool moderarAntes = false,
        int comentariosPorHora = 50,
        IContentStream? stream = null)
    {
        Semilla = semilla;
        Grafo = new StubSocialGraphService(null, null, semilla);
        Reacciones = new StubReactionService(null, null, semilla);
        Stream = stream ?? new StubContentStream(Grafo, Reacciones, null, null, null, semilla);
        Perfiles = new StubSocialProfileProjection(semilla);
        Identidad = IdentidadDePrueba.Con(miembros.ToArray());
        Notificaciones = new StubNotificationFeed(Grafo, Reacciones, null, semilla, Identidad);

        Directory.CreateDirectory(_carpeta);
        Comentarios = new FileSystemCommentRepository(
            Options.Create(new CommentsSettings
            {
                StorageRoot = "App_Data/syn-comments/",
                RequireModeration = moderarAntes,
                MaxBodyLengthChars = 2000,
            }),
            new Carpeta(_carpeta),
            NullLogger<FileSystemCommentRepository>.Instance);
        _limitador = new InMemoryFormRateLimiter(Options.Create(new FormsSettings { MaxSubmissionsPerHourPerIp = comentariosPorHora }));
    }

    public SemillaDeLaRed Semilla { get; }
    public StubSocialGraphService Grafo { get; }
    public StubReactionService Reacciones { get; }
    public IContentStream Stream { get; }
    public StubSocialProfileProjection Perfiles { get; }
    public IdentidadPublicaDeMiembros Identidad { get; }
    public StubNotificationFeed Notificaciones { get; }
    public StubMessagingService Mensajeria { get; } = new();
    public StubUserCollection Colecciones { get; } = new();
    public FileSystemCommentRepository Comentarios { get; }
    public ICommentModerationNotifier Moderacion { get; } = Substitute.For<ICommentModerationNotifier>();

    /// <summary>Los dos controladores de la red, con la sesión de <paramref name="correo"/> (o sin sesión).</summary>
    public (BlogsController Red, BlogsCommentsController Hilo) Como(string? correo)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(correo is not null);
        gate.CurrentMemberEmail.Returns(correo);

        var red = new BlogsController(
            Stream, Grafo, Reacciones, Perfiles, Comentarios, Mensajeria, Colecciones, Notificaciones, gate, Identidad);
        var hilo = new BlogsCommentsController(
            Stream, Comentarios, Perfiles, gate, Identidad, _limitador, Moderacion, Substitute.For<IAnalyticsTracker>())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        return (red, hilo);
    }

    /// <summary>El JSON tal como sale por el cable.</summary>
    public static string Cable(IActionResult resultado)
    {
        var ok = Assert.IsType<OkObjectResult>(resultado);
        return JsonSerializer.Serialize(ok.Value, ok.Value!.GetType(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    /// <summary>El cuerpo de una respuesta 200.</summary>
    public static T Cuerpo<T>(IActionResult resultado) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(resultado).Value);

    public void Dispose()
    {
        Grafo.Dispose();
        Reacciones.Dispose();
        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Mejor esfuerzo: la carpeta es temporal.
        }
    }

    private sealed class Carpeta : IHostEnvironment
    {
        public Carpeta(string raiz) => ContentRootPath = raiz;

        public string ApplicationName { get; set; } = "Synergos.CMS.Tests";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string ContentRootPath { get; set; }
        public string EnvironmentName { get; set; } = "Tests";
    }
}
