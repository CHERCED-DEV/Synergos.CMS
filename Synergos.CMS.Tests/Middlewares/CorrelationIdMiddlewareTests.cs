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
    public async Task InvokeAsync_HonoursIncomingHeader_CleanedWithTheSharedRule()
    {
        // ADR 0140 F3: la regla de Synergos.Shared.Correlation —letras y dígitos ASCII, hasta 32—, la
        // misma con que el orquestador la registra; antes el guion pasaba y los dos árboles divergían.
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "trace-abc";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("traceabc", context.Items[CorrelationIdMiddleware.ContextKey]);
    }

    [Fact]
    public async Task InvokeAsync_DoesNotCopyLongOrMultilineIncomingHeaders()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.HeaderName] = "abc\r\nFALSO: línea inyectada " + new string('x', 500);
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        var stored = Assert.IsType<string>(context.Items[CorrelationIdMiddleware.ContextKey]);
        Assert.Equal(32, stored.Length);
        Assert.Matches("^[A-Za-z0-9]+$", stored);
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
