using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

public class HealthControllerTests
{
    private sealed class FakeProbe : ISchemaHealthProbe
    {
        private readonly SchemaHealthResult _result;
        public FakeProbe(string name, bool healthy, string? message = null) =>
            _result = new SchemaHealthResult(name, healthy, message);
        public Task<SchemaHealthResult> CheckAsync(CancellationToken ct = default) =>
            Task.FromResult(_result);
    }

    /// <summary>Configuración con —o sin— el SHA que la imagen inyecta.</summary>
    private static IConfiguration Config(string? sha = null) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(sha is null
                ? new Dictionary<string, string?>()
                : new Dictionary<string, string?> { ["SYNERGOS_BUILD_SHA"] = sha })
            .Build();

    /// <summary>Lee una propiedad del payload anónimo del endpoint.</summary>
    private static string? Leer(object? payload, string propiedad) =>
        payload?.GetType().GetProperty(propiedad)?.GetValue(payload)?.ToString();

    [Fact]
    public async Task GetAsync_Returns200_WhenAllProbesHealthy()
    {
        var controller = new HealthController(new ISchemaHealthProbe[]
        {
            new FakeProbe("a", healthy: true),
            new FakeProbe("b", healthy: true),
        }, Config());

        var result = await controller.GetAsync(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetAsync_Returns503_WhenAnyProbeUnhealthy()
    {
        var controller = new HealthController(new ISchemaHealthProbe[]
        {
            new FakeProbe("a", healthy: true),
            new FakeProbe("b", healthy: false, message: "missing folder"),
        }, Config());

        var result = await controller.GetAsync(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetAsync_Returns200_WhenNoProbesRegistered()
    {
        var controller = new HealthController(Array.Empty<ISchemaHealthProbe>(), Config());

        var result = await controller.GetAsync(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
    }

    // ── La versión que contesta (HU #19) ────────────────────────────────────

    [Fact]
    public async Task GetAsync_ReportaElSha_QueLaImagenInyecto()
    {
        // Es lo que permite al humo distinguir "el sitio responde" de "el sitio responde con lo
        // que acabo de subir". Sin esto, un reinicio fallido deja viva la versión anterior y el
        // despliegue se da por bueno.
        var controller = new HealthController(
            new ISchemaHealthProbe[] { new FakeProbe("a", healthy: true) },
            Config("abc123def456"));

        var result = Assert.IsType<ObjectResult>(await controller.GetAsync(CancellationToken.None));

        Assert.Equal("abc123def456", Leer(result.Value, "version"));
    }

    [Fact]
    public async Task GetAsync_DiceQueNoSabeLaVersion_FueraDeLaImagen()
    {
        // Un `dotnet run` local no tiene SHA que reportar. Decirlo es mejor que inventar uno:
        // una versión falsa haría pasar el humo contra un despliegue que nunca se actualizó.
        var controller = new HealthController(Array.Empty<ISchemaHealthProbe>(), Config());

        var result = Assert.IsType<ObjectResult>(await controller.GetAsync(CancellationToken.None));

        Assert.Equal(HealthController.VersionDesconocida, Leer(result.Value, "version"));
    }

    [Fact]
    public async Task GetAsync_ReportaLaVersion_AunqueUnaProbeEsteRoja()
    {
        // El caso que de verdad importa en producción: `/_health` responde 503 por diseño
        // mientras el registry del CDN no esté montado. Si la versión desapareciera del cuerpo
        // en ese estado, el humo no podría verificar NADA justo en la configuración por defecto.
        var controller = new HealthController(
            new ISchemaHealthProbe[] { new FakeProbe("bundle_registry", healthy: false, message: "sin CDN") },
            Config("sha-de-la-imagen"));

        var result = Assert.IsType<ObjectResult>(await controller.GetAsync(CancellationToken.None));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Equal("sha-de-la-imagen", Leer(result.Value, "version"));
    }
}
