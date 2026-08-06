using Microsoft.AspNetCore.Http;
using Synergos.CMS.Web.Middlewares;

namespace Synergos.CMS.Tests.Middlewares;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_GeneratesCorrelationId_WhenHeaderAbsent()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.True(context.Items.ContainsKey(CorrelationIdMiddleware.ContextKey));
        var stored = Assert.IsType<string>(context.Items[CorrelationIdMiddleware.ContextKey]);
        Assert.False(string.IsNullOrWhiteSpace(stored));
    }

    [Fact]
    public async Task InvokeAsync_HonoursIncomingHeader()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "trace-abc";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("trace-abc", context.Items[CorrelationIdMiddleware.ContextKey]);
    }

    [Fact]
    public async Task InvokeAsync_IgnoresBlankIncomingHeader_AndGenerates()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "   ";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var stored = Assert.IsType<string>(context.Items[CorrelationIdMiddleware.ContextKey]);
        Assert.NotEqual("   ", stored);
        Assert.False(string.IsNullOrWhiteSpace(stored));
    }

    [Fact]
    public void ResolveCorrelationId_ReturnsGuid_WhenNoSignals()
    {
        var context = new DefaultHttpContext();

        var id = CorrelationIdMiddleware.ResolveCorrelationId(context);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(id.Length >= 16);
    }
}
