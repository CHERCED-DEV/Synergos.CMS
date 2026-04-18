using Microsoft.AspNetCore.Http;
using Synergos.CMS.Web.Middlewares;

namespace Synergos.CMS.Tests.Middlewares;

public class TimeoutMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_PassesThrough_WhenPipelineCompletesInTime()
    {
        var context = new DefaultHttpContext();
        var middleware = new TimeoutMiddleware(_ => Task.CompletedTask, TimeSpan.FromSeconds(1));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_SetsGatewayTimeout_WhenDownstreamExceedsBudget()
    {
        var context = new DefaultHttpContext();
        var middleware = new TimeoutMiddleware(
            next: async ctx => await Task.Delay(TimeSpan.FromSeconds(2), ctx.RequestAborted),
            timeout: TimeSpan.FromMilliseconds(50));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task InvokeAsync_RestoresOriginalRequestAbortedToken()
    {
        var context = new DefaultHttpContext();
        var original = context.RequestAborted;
        var middleware = new TimeoutMiddleware(_ => Task.CompletedTask, TimeSpan.FromSeconds(1));

        await middleware.InvokeAsync(context);

        Assert.Equal(original, context.RequestAborted);
    }

    [Fact]
    public void Constructor_ThrowsOnNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeoutMiddleware(_ => Task.CompletedTask, TimeSpan.Zero));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new TimeoutMiddleware(_ => Task.CompletedTask, TimeSpan.FromMilliseconds(-1)));
    }
}
