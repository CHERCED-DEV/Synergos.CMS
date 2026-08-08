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

    public Result<Message> Post(string threadId, Ref from, string? body, IReadOnlyList<Ref> attachments,
        IdempotencyKey key, DateTimeOffset? acknowledgeBeforeUtc = null)
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
            var ahora = _clock.GetUtcNow();
            // El autor cuenta como que ya accedió —si no, su propio mensaje le aparecería sin
            // leer y el contador de pendientes nunca llegaría a cero—. Y se anota con
            // `CmsSession`, que es lo único que se puede sostener: el campo NO mide cuánta
            // confianza tenemos en que escribió él, mide QUIÉN DIO FE. Acá nadie emitió un
            // token: `from` llegó declarado por el llamador sobre la llave compartida, igual
            // que en cualquier otra llamada.
            var mensaje = new Message(id, threadId, from, (body ?? string.Empty).Trim(), attachments,
                new[] { new Acknowledgment(from, ahora, IdentityAssertion.CmsSession) }, ahora,
                acknowledgeBeforeUtc);

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

    /// <summary>
    /// Registra que alguien accedió a un mensaje: quién, cuándo y con qué afirmación de identidad.
    /// </summary>
    /// <remarks>
    /// <para><b>El PRIMER acceso es el que cuenta, y por eso el instante no se sobreescribe.</b>
    /// Un segundo acceso no es un error —la persona puede abrir el mismo acto diez veces— pero
    /// tampoco es un dato nuevo: el término legal empezó a correr con el primero. Sobreescribir
    /// lo correría hacia adelante cada vez que alguien vuelve a mirar, que es justo lo contrario
    /// de lo que un acuse tiene que garantizar.</para>
    ///
    /// <para><b>Devuelve el mensaje completo y no solo el acuse</b> para que el llamador pueda
    /// ver el instante que quedó registrado — que puede no ser el que acaba de pedir.</para>
    /// </remarks>
    public Result<Message> Acknowledge(string messageId, Ref who, IdentityAssertion? assertion)
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
                : MessagingRules.CheckAcknowledge(hilo, who, assertion);
            if (motivo is not null) return Result.Rejected<Message>(motivo);

            // Idempotente, y el instante del primero se conserva intacto. Va ANTES del plazo a
            // propósito: quien accedió a tiempo y vuelve tres meses después tiene que recibir su
            // propio acuse, no un rechazo por un plazo que él ya había cumplido.
            if (mensaje.AcknowledgmentOf(who) is not null) return Result.Ok(mensaje);

            var ahora = _clock.GetUtcNow();
            var vencido = MessagingRules.CheckAcknowledgeWindow(mensaje, ahora);
            if (vencido is not null) return Result.Rejected<Message>(vencido);

            var acusado = mensaje with
            {
                Acknowledgments = mensaje.Acknowledgments
                    .Append(new Acknowledgment(who, ahora, assertion!.Value))
                    .ToList(),
            };
            _messages.Put(acusado);
            return Result.Ok(acusado);
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
