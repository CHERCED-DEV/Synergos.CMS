using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="SseRealtimeHub"/> (seam <c>IRealtimeNotifier</c>, T7 — el fan-out en
/// vivo): empty (publicar sin suscriptores NO lanza — es el caso normal) / happy (el
/// suscriptor recibe lo publicado) / filter (cada canal recibe SOLO lo suyo) /
/// idempotent (desuscribir dos veces no rompe). Más las dos propiedades que evitan que
/// un cliente lento tumbe el servidor: cola acotada y limpieza del canal vacío.
/// ADR 0075, ADR 0111.
/// </summary>
public sealed class SseRealtimeHubTests
{
    private const string Channel = "eventos:checkin:evt-1";

    private static SseRealtimeHub Make() => new(NullLogger<SseRealtimeHub>.Instance);

    [Fact] // empty: publicar sin nadie escuchando es NORMAL, no un error
    public async Task Publish_NoSubscribers_DoesNotThrow()
    {
        var hub = Make();

        await hub.PublishAsync(Channel, "checkin", "{}");
        await hub.PublishAsync("canal-que-no-existe", "x", "{}");

        Assert.Equal(0, hub.SubscriberCount(Channel));
    }

    [Fact] // happy: el suscriptor recibe lo que se publica, con su nombre de evento
    public async Task Publish_Subscriber_ReceivesMessage()
    {
        var hub = Make();
        var reader = hub.Subscribe(Channel, out var unsubscribe);
        try
        {
            await hub.PublishAsync(Channel, "checkin", "{\"status\":\"valid\"}");

            var message = await reader.ReadAsync(new CancellationTokenSource(2000).Token);
            Assert.Equal("checkin", message.EventName);
            Assert.Contains("valid", message.PayloadJson, System.StringComparison.Ordinal);
        }
        finally
        {
            unsubscribe();
        }
    }

    [Fact] // happy: TODOS los suscriptores del canal reciben (varias puertas del evento)
    public async Task Publish_ManySubscribers_AllReceive()
    {
        var hub = Make();
        var a = hub.Subscribe(Channel, out var offA);
        var b = hub.Subscribe(Channel, out var offB);
        try
        {
            Assert.Equal(2, hub.SubscriberCount(Channel));
            await hub.PublishAsync(Channel, "checkin", "{}");

            Assert.Equal("checkin", (await a.ReadAsync(new CancellationTokenSource(2000).Token)).EventName);
            Assert.Equal("checkin", (await b.ReadAsync(new CancellationTokenSource(2000).Token)).EventName);
        }
        finally
        {
            offA();
            offB();
        }
    }

    [Fact] // filter: EL CASO QUE IMPORTA — un canal no ve lo de otro evento
    public async Task Publish_OtherChannel_DoesNotLeak()
    {
        var hub = Make();
        var mine = hub.Subscribe(Channel, out var off);
        try
        {
            await hub.PublishAsync("eventos:checkin:OTRO-EVENTO", "checkin", "{\"ajeno\":true}");

            // Nada que leer: no llegó lo del otro evento.
            Assert.False(mine.TryRead(out _));
        }
        finally
        {
            off();
        }
    }

    [Fact] // Un cliente que NO lee no hace crecer la memoria: la cola descarta lo viejo
    public async Task Publish_SlowSubscriber_DropsOldestInsteadOfGrowing()
    {
        var hub = Make();
        var reader = hub.Subscribe(Channel, out var off);
        try
        {
            // Muy por encima de la capacidad (32) sin leer nada.
            for (var i = 0; i < 200; i++)
            {
                await hub.PublishAsync(Channel, "checkin", $"{{\"n\":{i}}}");
            }

            var drained = 0;
            while (reader.TryRead(out _))
            {
                drained++;
            }

            Assert.True(drained <= 32, $"la cola no debe crecer sin límite (drenados: {drained})");
            Assert.True(drained > 0, "debe conservar los últimos mensajes");
        }
        finally
        {
            off();
        }
    }

    [Fact] // El canal sin suscriptores se retira: no se acumulan claves muertas
    public void Unsubscribe_LastSubscriber_RemovesChannel()
    {
        var hub = Make();
        hub.Subscribe(Channel, out var off);
        Assert.Equal(1, hub.SubscriberCount(Channel));

        off();

        Assert.Equal(0, hub.SubscriberCount(Channel));
    }

    [Fact] // idempotent: desuscribir dos veces no rompe
    public void Unsubscribe_Twice_IsSafe()
    {
        var hub = Make();
        hub.Subscribe(Channel, out var off);

        off();
        off();

        Assert.Equal(0, hub.SubscriberCount(Channel));
    }

    [Fact] // Un suscriptor que se va no impide que los demás sigan recibiendo
    public async Task Unsubscribe_One_OthersKeepReceiving()
    {
        var hub = Make();
        var a = hub.Subscribe(Channel, out var offA);
        _ = hub.Subscribe(Channel, out var offB);

        offB();
        try
        {
            await hub.PublishAsync(Channel, "checkin", "{}");
            Assert.Equal("checkin", (await a.ReadAsync(new CancellationTokenSource(2000).Token)).EventName);
        }
        finally
        {
            offA();
        }
    }
}
