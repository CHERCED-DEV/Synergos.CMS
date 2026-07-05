using System.Security.Cryptography;
using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IMessagingService"/> — hilos 1:1 (comprador↔vendedor,
/// huésped↔host, interesado↔agente…) en memoria del proceso. Seam GENÉRICO
/// del plan doc 21 §1.4 (P7, v1 simple: sin typing/grupos/read-receipts).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Determinista: el ThreadId se DERIVA de
/// (contextRef + par de participantes normalizado) vía SHA-256, por lo que
/// <see cref="StartThreadAsync"/> es idempotente a nivel de hilo — re-iniciar
/// la misma conversación agrega el mensaje al hilo existente en vez de crear
/// uno paralelo. Los MessageId son secuenciales por hilo (deterministas).
/// Estado en memoria (proceso), suficiente para demo; un adapter real delega
/// a DB/chat backend. Time source inyectable para tests (ADR 0075).
/// </remarks>
public sealed class StubMessagingService : IMessagingService
{
    private readonly Func<DateTimeOffset> _now;
    private readonly object _gate = new();
    private readonly Dictionary<string, ThreadState> _threads = new(StringComparer.Ordinal);

    public StubMessagingService()
        : this(null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests. Null = reloj real.</summary>
    public StubMessagingService(Func<DateTimeOffset>? now)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<MessageThread> StartThreadAsync(
        string contextRef,
        string from,
        string to,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(contextRef))
        {
            throw new ArgumentException("El contexto del hilo es obligatorio.", nameof(contextRef));
        }
        var sender = RequireParticipant(from, nameof(from));
        var recipient = RequireParticipant(to, nameof(to));
        if (string.Equals(sender, recipient, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("El remitente y el destinatario deben ser distintos.", nameof(to));
        }
        var text = RequireBody(body);

        var context = contextRef.Trim();
        var threadId = BuildThreadId(context, sender, recipient);

        lock (_gate)
        {
            if (!_threads.TryGetValue(threadId, out var state))
            {
                state = new ThreadState(
                    ThreadId: threadId,
                    ContextRef: context,
                    Participants: new[] { sender, recipient },
                    Messages: new List<ThreadMessage>(),
                    CreatedAt: _now());
                _threads[threadId] = state;
            }

            Append(state, sender, text);
            return Task.FromResult(ToThread(state));
        }
    }

    public Task<MessageThread> ReplyAsync(
        string threadId,
        string from,
        string body,
        CancellationToken cancellationToken = default)
    {
        var sender = RequireParticipant(from, nameof(from));
        var text = RequireBody(body);

        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(threadId) || !_threads.TryGetValue(threadId.Trim(), out var state))
            {
                throw new ArgumentException("Hilo no encontrado.", nameof(threadId));
            }
            if (!state.Participants.Any(p => string.Equals(p, sender, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"'{sender}' no es participante del hilo {state.ThreadId}.", nameof(from));
            }

            Append(state, sender, text);
            return Task.FromResult(ToThread(state));
        }
    }

    public Task<MessageThread?> GetThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (string.IsNullOrWhiteSpace(threadId) || !_threads.TryGetValue(threadId.Trim(), out var state))
            {
                return Task.FromResult<MessageThread?>(null);
            }
            return Task.FromResult<MessageThread?>(ToThread(state));
        }
    }

    public Task<IReadOnlyList<MessageThreadSummary>> GetInboxAsync(
        string participant,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(participant))
        {
            return Task.FromResult<IReadOnlyList<MessageThreadSummary>>(Array.Empty<MessageThreadSummary>());
        }

        var who = participant.Trim();
        lock (_gate)
        {
            var summaries = _threads.Values
                .Where(t => t.Participants.Any(p => string.Equals(p, who, StringComparison.OrdinalIgnoreCase)))
                .Where(t => t.Messages.Count > 0)
                .Select(t => new MessageThreadSummary(
                    ThreadId: t.ThreadId,
                    ContextRef: t.ContextRef,
                    Participants: t.Participants.ToList(),
                    LastMessagePreview: Preview(t.Messages[^1].Body),
                    LastMessageAt: t.Messages[^1].SentAt,
                    MessageCount: t.Messages.Count))
                .OrderByDescending(s => s.LastMessageAt)
                .ThenBy(s => s.ThreadId, StringComparer.Ordinal)
                .ToList();
            return Task.FromResult<IReadOnlyList<MessageThreadSummary>>(summaries);
        }
    }

    // ThreadId determinista: contexto + participantes ordenados (case-insensitive)
    // → SHA-256 → primeros 16 hex. Mismo (contexto, par) = mismo hilo, siempre.
    private static string BuildThreadId(string contextRef, string a, string b)
    {
        var pair = new[] { a.ToLowerInvariant(), b.ToLowerInvariant() }
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        var seed = $"{contextRef.ToLowerInvariant()}|{pair[0]}|{pair[1]}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        return "thr_" + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }

    private void Append(ThreadState state, string sender, string body)
    {
        state.Messages.Add(new ThreadMessage(
            MessageId: $"{state.ThreadId}-m{state.Messages.Count + 1}",
            From: sender,
            Body: body,
            SentAt: _now()));
    }

    private static MessageThread ToThread(ThreadState state) => new(
        ThreadId: state.ThreadId,
        ContextRef: state.ContextRef,
        Participants: state.Participants.ToList(),
        Messages: state.Messages.ToList(),
        CreatedAt: state.CreatedAt,
        LastMessageAt: state.Messages.Count > 0 ? state.Messages[^1].SentAt : state.CreatedAt);

    private static string Preview(string body)
        => body.Length <= 80 ? body : body[..77] + "…";

    private static string RequireParticipant(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("El participante es obligatorio.", paramName);
        }
        return value.Trim();
    }

    private static string RequireBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ArgumentException("El mensaje no puede estar vacío.", nameof(body));
        }
        return body.Trim();
    }

    private sealed record ThreadState(
        string ThreadId,
        string ContextRef,
        IReadOnlyList<string> Participants,
        List<ThreadMessage> Messages,
        DateTimeOffset CreatedAt);
}
