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
using Microsoft.Extensions.DependencyInjection;
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
            // Con el reloj del SISTEMA, y sin carrera: el trabajo espera a que venza el plazo, tarde lo que tarde el
            // temporizador en tener un hilo.
            next: async ctx => await Task.Delay(Timeout.InfiniteTimeSpan, ctx.RequestAborted),
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
        var reloj = new RelojManual();
        var middleware = new TimeoutMiddleware(
            next: ctx =>
            {
                reloj.Avanzar(TimeSpan.FromMilliseconds(300));
                return EscribirJsonComoMvc(ctx, new { pagesFilled = 8 });
            },
            timeout: TimeSpan.FromMilliseconds(50),
            reloj: reloj);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status504GatewayTimeout, context.Response.StatusCode);
    }

    [Fact]
    public async Task Quien_apaga_el_plazo_termina_y_contesta_lo_que_hizo()
    {
        var context = ConCuerpo();
        var reloj = new RelojManual();
        var middleware = new TimeoutMiddleware(
            next: ctx =>
            {
                ctx.Features.Get<IHttpRequestTimeoutFeature>()!.DisableTimeout();
                reloj.Avanzar(TimeSpan.FromMilliseconds(300));
                return EscribirJsonComoMvc(ctx, new { pagesFilled = 8 });
            },
            timeout: TimeSpan.FromMilliseconds(50),
            reloj: reloj);

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Contains("\"pagesFilled\":8", Cuerpo(context));
    }

    [Fact]
    public async Task Una_respuesta_que_ya_empezo_no_se_toca_aunque_venza_el_plazo()
    {
        var context = new DefaultHttpContext();
        context.Features.Set<IHttpResponseFeature>(new RespuestaEmpezada());
        var reloj = new RelojManual();
        var middleware = new TimeoutMiddleware(
            next: ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status200OK;
                reloj.Avanzar(TimeSpan.FromMilliseconds(300));
                Assert.True(ctx.RequestAborted.IsCancellationRequested, "el plazo tenía que haber vencido al avanzar el reloj");
                return Task.CompletedTask;
            },
            timeout: TimeSpan.FromMilliseconds(50),
            reloj: reloj);

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

    [Fact]
    public void El_pipeline_la_arma_con_un_solo_argumento_aunque_haya_un_reloj_registrado()
    {
        // Program.cs hace UseMiddleware<TimeoutMiddleware>() sin argumentos: con tres constructores y un TimeProvider en el
        // contenedor, el activador tiene que seguir eligiendo el del plazo de siempre, sin ambigüedad.
        using var servicios = new ServiceCollection().AddSingleton(TimeProvider.System).BuildServiceProvider();

        var middleware = ActivatorUtilities.CreateInstance<TimeoutMiddleware>(servicios, (RequestDelegate)(_ => Task.CompletedTask));

        Assert.NotNull(middleware);
    }

    /// <summary>
    /// Un reloj que sólo avanza cuando la prueba lo dice: el plazo vence en el instante en que se avanza, y no cuando el pool
    /// de hilos tenga uno libre para el temporizador del sistema (lo que hacía intermitente la prueba del sembrador en el CI).
    /// </summary>
    private sealed class RelojManual : TimeProvider
    {
        private readonly List<Temporizador> _temporizadores = [];
        private DateTimeOffset _ahora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _ahora;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var temporizador = new Temporizador(this, callback, state);
            temporizador.Change(dueTime, period);
            lock (_temporizadores) _temporizadores.Add(temporizador);
            return temporizador;
        }

        /// <summary>Pasa <paramref name="cuanto"/> y dispara, en este hilo, lo que venció.</summary>
        public void Avanzar(TimeSpan cuanto)
        {
            _ahora += cuanto;
            List<Temporizador> vencidos;
            lock (_temporizadores) vencidos = _temporizadores.Where(t => t.Vence is { } v && v <= _ahora).ToList();
            foreach (var t in vencidos) t.Disparar();
        }

        private sealed class Temporizador(RelojManual reloj, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? Vence { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                Vence = dueTime == Timeout.InfiniteTimeSpan ? null : reloj._ahora + dueTime;
                return true;
            }

            public void Disparar()
            {
                Vence = null;
                callback(state);
            }

            public void Dispose() => Vence = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
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
