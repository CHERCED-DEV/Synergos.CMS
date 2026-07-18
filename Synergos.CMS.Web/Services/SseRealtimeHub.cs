using System.Collections.Concurrent;
using System.Threading.Channels;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>Un mensaje listo para escribirse como evento SSE.</summary>
public sealed record RealtimeMessage(string EventName, string PayloadJson);

/// <summary>
/// Default <see cref="IRealtimeNotifier"/> (T7) + registro de suscriptores. Fan-out EN
/// PROCESO sobre <see cref="Channel{T}"/>: publicar escribe en la cola de cada
/// suscriptor del canal; el endpoint SSE las drena y las manda al navegador.
/// </summary>
/// <remarks>
/// <para><b>Cada suscriptor tiene su propia cola ACOTADA</b> (<c>BoundedChannel</c> con
/// <c>DropOldest</c>). Es deliberado: un cliente lento —una pestaña en segundo plano, una
/// red mala— no puede hacer crecer la memoria del servidor sin límite ni frenar a los
/// demás. Pierde los mensajes viejos, que es exactamente lo que un feed en vivo debe
/// hacer: interesa lo último, no la historia completa.</para>
/// <para><b>Publicar nunca lanza.</b> Un canal sin suscriptores es el caso normal (nadie
/// mirando la consola), no un error; y una cola llena descarta, no revienta. El hecho de
/// negocio ya ocurrió y está persistido.</para>
/// <para>La limpieza es explícita: el endpoint desuscribe en su <c>finally</c>. Un canal
/// que se queda sin suscriptores se borra del diccionario para no acumular claves
/// muertas (un evento por día durante un año son 365 entradas huérfanas si no).</para>
/// </remarks>
public sealed class SseRealtimeHub : IRealtimeNotifier
{
    /// <summary>
    /// Cuántos mensajes se le guardan a un suscriptor que no lee. Pequeño a propósito:
    /// esto es un feed en vivo, no una bandeja.
    /// </summary>
    private const int SubscriberQueueCapacity = 32;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<RealtimeMessage>>> _channels =
        new(StringComparer.Ordinal);

    private readonly ILogger<SseRealtimeHub> _logger;

    public SseRealtimeHub(ILogger<SseRealtimeHub> logger) => _logger = logger;

    /// <summary>Suscriptores actuales de un canal (para tests y diagnóstico).</summary>
    public int SubscriberCount(string channel) =>
        _channels.TryGetValue(channel, out var subs) ? subs.Count : 0;

    public Task PublishAsync(
        string channel,
        string eventName,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(channel) || !_channels.TryGetValue(channel, out var subscribers))
        {
            // Nadie escuchando: es el caso NORMAL, no un fallo.
            return Task.CompletedTask;
        }

        var message = new RealtimeMessage(eventName, payloadJson);
        foreach (var subscriber in subscribers.Values)
        {
            // DropOldest: si la cola está llena, TryWrite igual acepta descartando el
            // más viejo. No se espera al lector — un cliente lento no frena la operación.
            subscriber.Writer.TryWrite(message);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Registra un suscriptor y devuelve su cola + el token para darse de baja. El
    /// llamador DEBE llamar a <paramref name="unsubscribe"/> al terminar (finally).
    /// </summary>
    public ChannelReader<RealtimeMessage> Subscribe(string channel, out Action unsubscribe)
    {
        var id = Guid.NewGuid();
        var queue = Channel.CreateBounded<RealtimeMessage>(new BoundedChannelOptions(SubscriberQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        var subscribers = _channels.GetOrAdd(channel, _ => new ConcurrentDictionary<Guid, Channel<RealtimeMessage>>());
        subscribers[id] = queue;

        unsubscribe = () =>
        {
            if (!_channels.TryGetValue(channel, out var current))
            {
                return;
            }
            current.TryRemove(id, out _);
            if (current.IsEmpty)
            {
                // Sin suscriptores el canal se retira: evita acumular claves muertas.
                // TryRemove con el par exacto para no borrar un canal que acaba de
                // recibir un suscriptor nuevo entre estas dos líneas.
                _channels.TryRemove(new KeyValuePair<string, ConcurrentDictionary<Guid, Channel<RealtimeMessage>>>(
                    channel, current));
            }
            queue.Writer.TryComplete();
        };

        _logger.LogDebug("Realtime: suscriptor {Id} en {Channel}", id, channel);
        return queue.Reader;
    }
}
