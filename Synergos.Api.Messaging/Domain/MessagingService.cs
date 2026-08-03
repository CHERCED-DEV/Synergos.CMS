using Synergos.Api.Messaging.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Messaging.Domain;

/// <summary>Compone las reglas de <see cref="MessagingRules"/> con los almacenes.</summary>
public sealed class MessagingService
{
    private readonly IThreadStore _threads;
    private readonly IMessageStore _messages;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public MessagingService(IThreadStore threads, IMessageStore messages, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _threads = threads;
        _messages = messages;
        _idempotency = idempotency;
        _clock = clock;
    }

    public Result<MessageThread> OpenThread(Ref topic, IReadOnlyList<Ref> participants, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("thread", key) is { } yaEra)
            {
                return _threads.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{MessagingRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el hilo no está.");
            }

            var motivo = MessagingRules.CheckOpen(participants);
            if (motivo is not null) return Result.Rejected<MessageThread>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var hilo = new MessageThread(id, topic, participants, Closed: false, _clock.GetUtcNow());
            _threads.Put(hilo);
            _idempotency.Remember("thread", key, id);
            return Result.Ok(hilo);
        }
    }

    public Result<MessageThread> GetThread(string id, Ref? who)
    {
        var hilo = _threads.Find(id);
        if (hilo is null)
        {
            return Rejection.NotFound($"{MessagingRules.CodePrefix}.thread_not_found", $"No existe el hilo {id}.");
        }
        if (who is null)
        {
            return Rejection.Invalid($"{MessagingRules.CodePrefix}.who_required",
                "Hace falta decir quién pregunta: un hilo solo lo ven sus participantes.");
        }

        var motivo = MessagingRules.CheckRead(hilo, who);
        return motivo is not null ? Result.Rejected<MessageThread>(motivo) : Result.Ok(hilo);
    }

    public Result<MessageThread> CloseThread(string id)
    {
        lock (_gate)
        {
            var hilo = _threads.Find(id);
            if (hilo is null)
            {
                return Rejection.NotFound($"{MessagingRules.CodePrefix}.thread_not_found", $"No existe el hilo {id}.");
            }
            if (hilo.Closed) return Result.Ok(hilo);   // idempotente

            var cerrado = hilo with { Closed = true };
            _threads.Put(cerrado);
            return Result.Ok(cerrado);
        }
    }

    public Result<Message> Post(string threadId, Ref from, string? body, IReadOnlyList<Ref> attachments, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("message", key) is { } yaEra)
            {
                return _messages.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{MessagingRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el mensaje no está.");
            }

            var hilo = _threads.Find(threadId);
            if (hilo is null)
            {
                return Rejection.NotFound($"{MessagingRules.CodePrefix}.thread_not_found", $"No existe el hilo {threadId}.");
            }

            var motivo = MessagingRules.CheckPost(hilo, from, body, attachments);
            if (motivo is not null) return Result.Rejected<Message>(motivo);

            var id = Guid.NewGuid().ToString("n");
            // Quien escribe cuenta como que ya lo leyó: si no, su propio mensaje le aparecería
            // sin leer y el contador de pendientes nunca llegaría a cero.
            var mensaje = new Message(id, threadId, from, (body ?? string.Empty).Trim(), attachments, new[] { from }, _clock.GetUtcNow());

            _messages.Put(mensaje);
            _idempotency.Remember("message", key, id);
            return Result.Ok(mensaje);
        }
    }

    public Result<Page<Message>> ListMessages(string threadId, Ref? who, int offset, int limit)
    {
        var hilo = GetThread(threadId, who);
        if (!hilo.IsOk) return Result.Rejected<Page<Message>>(hilo.Rejection!);

        var todos = _messages.InThread(threadId);
        return Result.Ok(new Page<Message>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }

    /// <summary>Marca un mensaje como leído por alguien.</summary>
    public Result<Message> MarkRead(string messageId, Ref who)
    {
        lock (_gate)
        {
            var mensaje = _messages.Find(messageId);
            if (mensaje is null)
            {
                return Rejection.NotFound($"{MessagingRules.CodePrefix}.message_not_found", $"No existe el mensaje {messageId}.");
            }

            var hilo = _threads.Find(mensaje.ThreadId);
            var motivo = hilo is null
                ? Rejection.Conflict($"{MessagingRules.CodePrefix}.thread_gone", "El mensaje apunta a un hilo que ya no está.")
                : MessagingRules.CheckRead(hilo, who);
            if (motivo is not null) return Result.Rejected<Message>(motivo);

            if (mensaje.ReadBy.Contains(who)) return Result.Ok(mensaje);   // idempotente

            var leido = mensaje with { ReadBy = mensaje.ReadBy.Append(who).ToList() };
            _messages.Put(leido);
            return Result.Ok(leido);
        }
    }

    public Result<Page<MessageThread>> ListThreads(Ref? who, int offset, int limit)
    {
        if (who is null)
        {
            return Rejection.Invalid($"{MessagingRules.CodePrefix}.who_required", "Hace falta decir de quién son los hilos.");
        }

        var todos = _threads.ForParticipant(who)
            .OrderByDescending(t => t.OpenedAtUtc)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<MessageThread>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }
}
