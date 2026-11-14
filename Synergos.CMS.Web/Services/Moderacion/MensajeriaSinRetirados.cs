using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Moderacion;
using Synergos.CMS.Web.Services.Lecturas;

namespace Synergos.CMS.Web.Services.Moderacion;

/// <summary>
/// La mensajería sin los mensajes directos de Blogs que la moderación retiró (012-blogs-social, I5): un mensaje retirado
/// no sale en su hilo, ni como el último de la bandeja, ni en lo que devuelve escribir. Sigue guardado, como un
/// comentario retirado.
/// </summary>
/// <remarks>
/// <para>Es la mensajería de todos (la comparten la tienda, Gobierno y Salud), y sólo mira los hilos de los mensajes
/// directos de Blogs (contexto <c>dm</c>): los demás pasan tal cual, sin leer el registro.</para>
/// <para>Un hilo al que no le queda ningún mensaje sale de la bandeja: no hay nada que mostrar de él.</para>
/// <para>Es dueña de la mensajería que envuelve, como <see cref="StreamSinRetirados"/> del stream: el contenedor ya no
/// la ve, así que la libera ella.</para>
/// </remarks>
public sealed class MensajeriaSinRetirados : IMessagingService, IDisposable
{
    private readonly IMessagingService _interna;
    private readonly IRegistroDeRetirados _retirados;

    /// <summary>Sobre la mensajería guardada y el registro de lo retirado.</summary>
    public MensajeriaSinRetirados(IMessagingService interna, IRegistroDeRetirados retirados)
    {
        _interna = interna ?? throw new ArgumentNullException(nameof(interna));
        _retirados = retirados ?? throw new ArgumentNullException(nameof(retirados));
    }

    /// <inheritdoc />
    public void Dispose() => (_interna as IDisposable)?.Dispose();

    /// <inheritdoc />
    public async Task<MessageThread> StartThreadAsync(
        string contextRef,
        string from,
        string to,
        string body,
        CancellationToken cancellationToken = default)
        => await SinRetiradosAsync(
                await _interna.StartThreadAsync(contextRef, from, to, body, cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<MessageThread> ReplyAsync(
        string threadId,
        string from,
        string body,
        CancellationToken cancellationToken = default)
        => await SinRetiradosAsync(
                await _interna.ReplyAsync(threadId, from, body, cancellationToken).ConfigureAwait(false),
                cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<MessageThread?> GetThreadAsync(string threadId, CancellationToken cancellationToken = default)
        => await _interna.GetThreadAsync(threadId, cancellationToken).ConfigureAwait(false) is { } hilo
            ? await SinRetiradosAsync(hilo, cancellationToken).ConfigureAwait(false)
            : null;

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageThreadSummary>> GetInboxAsync(
        string participant,
        CancellationToken cancellationToken = default)
    {
        var bandeja = await _interna.GetInboxAsync(participant, cancellationToken).ConfigureAwait(false);
        if (!bandeja.Any(h => EsDirecto(h.ContextRef)))
        {
            return bandeja;
        }

        var retirados = await _retirados.RetiradosAsync(MensajesModerables.TipoDelMensaje, cancellationToken)
            .ConfigureAwait(false);
        if (retirados.Count == 0)
        {
            return bandeja;
        }

        var quedan = new List<MessageThreadSummary>(bandeja.Count);
        foreach (var resumen in bandeja)
        {
            if (!EsDirecto(resumen.ContextRef)
                || !retirados.Any(r => r.StartsWith(resumen.ThreadId + ":", StringComparison.Ordinal)))
            {
                quedan.Add(resumen);
                continue;
            }

            var hilo = await _interna.GetThreadAsync(resumen.ThreadId, cancellationToken).ConfigureAwait(false);
            var mensajes = hilo is null ? [] : Visibles(hilo, retirados);
            if (mensajes.Count == 0)
            {
                continue;
            }

            var ultimo = mensajes[^1];
            quedan.Add(resumen with
            {
                LastMessagePreview = StubMessagingService.Preview(ultimo.Body),
                LastMessageAt = ultimo.SentAt,
                MessageCount = mensajes.Count,
            });
        }

        return quedan;
    }

    /// <summary>Si el hilo es un mensaje directo de Blogs, el contexto que modera esta pieza.</summary>
    private static bool EsDirecto(string? contexto)
        => !string.IsNullOrWhiteSpace(contexto)
            && (string.Equals(contexto, ContextoDeMensajesDeBlogs.Prefijo, StringComparison.OrdinalIgnoreCase)
                || contexto.StartsWith(ContextoDeMensajesDeBlogs.Prefijo + ":", StringComparison.OrdinalIgnoreCase));

    private async Task<MessageThread> SinRetiradosAsync(MessageThread hilo, CancellationToken cancellationToken)
    {
        if (!EsDirecto(hilo.ContextRef))
        {
            return hilo;
        }

        var retirados = await _retirados.RetiradosAsync(MensajesModerables.TipoDelMensaje, cancellationToken)
            .ConfigureAwait(false);
        var mensajes = Visibles(hilo, retirados);
        return mensajes.Count == hilo.Messages.Count
            ? hilo
            : hilo with
            {
                Messages = mensajes,
                LastMessageAt = mensajes.Count > 0 ? mensajes[^1].SentAt : hilo.CreatedAt,
            };
    }

    private static List<ThreadMessage> Visibles(MessageThread hilo, IReadOnlySet<string> retirados)
        => hilo.Messages.Where(m => !retirados.Contains($"{hilo.ThreadId}:{m.MessageId}")).ToList();
}
