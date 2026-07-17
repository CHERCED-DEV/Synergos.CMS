using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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

    [Fact]
    public async Task GetAsync_Returns200_WhenAllProbesHealthy()
    {
        var controller = new HealthController(new ISchemaHealthProbe[]
        {
            new FakeProbe("a", healthy: true),
            new FakeProbe("b", healthy: true),
        });

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
        });

        var result = await controller.GetAsync(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
    }

    [Fact]
    public async Task GetAsync_Returns200_WhenNoProbesRegistered()
    {
        var controller = new HealthController(Array.Empty<ISchemaHealthProbe>());

        var result = await controller.GetAsync(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, objectResult.StatusCode);
    }
}
