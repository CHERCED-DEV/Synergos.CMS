using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Filters;
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

    /// <summary>
    /// Lo que hace MVC al devolver <c>Ok(objeto)</c>: el formateador JSON real, con el token de la
    /// petición. Es el doble que entrega la ausencia como la entrega el sistema (#188): con el token
    /// cancelado no escribe NADA y no lanza.
    /// </summary>
    private static Task EscribirJsonComoMvc(HttpContext ctx, object valor)
        => new SystemTextJsonOutputFormatter(new JsonSerializerOptions(JsonSerializerDefaults.Web) { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }).WriteAsync(
            new OutputFormatterWriteContext(ctx, (s, e) => new StreamWriter(s, e), valor.GetType(), valor));

    private static DefaultHttpContext ConCuerpo()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        return ctx;
    }

    private static string Cuerpo(HttpContext ctx)
        => Encoding.UTF8.GetString(((MemoryStream)ctx.Response.Body).ToArray());

    [Fact]
    public async Task Un_trabajo_sincrono_que_vence_el_plazo_no_sale_como_200_con_el_cuerpo_vacio()
    {
        // El sembrador (#188): dos minutos en síncrono contra treinta segundos. El trabajo no mira el
        // token, así que termina; lo que el plazo rompía era la RESPUESTA. Medido en vivo: 200,
        // Content-Length 0, y el resultado sólo en el log.
        var context = ConCuerpo();
        var middleware = new TimeoutMiddleware(
            next: ctx =>
            {
                Thread.Sleep(TimeSpan.FromMilliseconds(300));
                return EscribirJsonComoMvc(ctx, new { pagesFilled = 8 });
            },
            timeout: TimeSpan.FromMilliseconds(50));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task Quien_apaga_el_plazo_termina_y_contesta_lo_que_hizo()
    {
        var context = ConCuerpo();
        var middleware = new TimeoutMiddleware(
            next: ctx =>
            {
                ctx.Features.Get<IHttpRequestTimeoutFeature>()!.DisableTimeout();
                Thread.Sleep(TimeSpan.FromMilliseconds(300));
                return EscribirJsonComoMvc(ctx, new { pagesFilled = 8 });
            },
            timeout: TimeSpan.FromMilliseconds(50));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("\"pagesFilled\":8", Cuerpo(context));
    }

    [Fact]
    public async Task Una_respuesta_que_ya_empezo_no_se_toca_aunque_venza_el_plazo()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new RespuestaEmpezada());
        var middleware = new TimeoutMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                Thread.Sleep(TimeSpan.FromMilliseconds(300));
                return Task.CompletedTask;
            },
            timeout: TimeSpan.FromMilliseconds(50));

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task El_rasgo_del_plazo_vive_lo_que_dura_la_peticion()
    {
        var context = new DefaultHttpContext();
        IHttpRequestTimeoutFeature? dentro = null;
        var middleware = new TimeoutMiddleware(
            next: ctx => { dentro = ctx.Features.Get<IHttpRequestTimeoutFeature>(); return Task.CompletedTask; },
            timeout: TimeSpan.FromSeconds(1));

        await middleware.InvokeAsync(context);

        Assert.NotNull(dentro);
        Assert.Null(context.Features.Get<IHttpRequestTimeoutFeature>());
    }

    [Fact]
    public void SinPlazoDePeticion_apaga_el_plazo_antes_de_que_empiece_el_trabajo()
    {
        var context = new DefaultHttpContext();
        var plazo = new PlazoEspia();
        context.Features.Set<IHttpRequestTimeoutFeature>(plazo);
        var recurso = new ResourceExecutingContext(
            new ActionContext(context, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

        new SinPlazoDePeticionAttribute().OnResourceExecuting(recurso);

        Assert.True(plazo.Apagado);
    }

    [Fact]
    public void El_sembrador_va_sin_plazo_de_peticion()
    {
        // Quitarlo vuelve al 200 con el cuerpo vacío: la siembra tarda minutos.
        Assert.NotNull(typeof(DevController)
            .GetCustomAttributes(typeof(SinPlazoDePeticionAttribute), inherit: false)
            .SingleOrDefault());
    }

    private sealed class RespuestaEmpezada : HttpResponseFeature
    {
        public override bool HasStarted => true;
    }

    private sealed class PlazoEspia : IHttpRequestTimeoutFeature
    {
        public bool Apagado { get; private set; }

        public CancellationToken RequestTimeoutToken => CancellationToken.None;

        public void DisableTimeout() => Apagado = true;
    }
}
