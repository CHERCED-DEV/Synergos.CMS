using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IMessagingService"/> — hilos 1:1 (comprador↔vendedor,
/// huésped↔host, interesado↔agente, DM de Blogs…). Seam GENÉRICO del plan doc 21
/// §1.4 (P7, v1 simple: sin typing/grupos/read-receipts).
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Determinista: el ThreadId se DERIVA de
/// (contextRef + par de participantes normalizado) vía SHA-256, por lo que
/// <see cref="StartThreadAsync"/> es idempotente a nivel de hilo — re-iniciar
/// la misma conversación agrega el mensaje al hilo existente en vez de crear
/// uno paralelo. Los MessageId son secuenciales por hilo (deterministas).
/// Time source inyectable para tests (ADR 0075).</para>
///
/// <para><b>Durabilidad.</b> Los hilos viven detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en un diccionario del
/// proceso: un reinicio borraba cada mensaje directo que alguien hubiera
/// mandado.</para>
///
/// <para><b>Un documento por hilo</b> (el ThreadId determinista es la clave, y
/// además es un nombre de archivo seguro por construcción). Mandar, responder y
/// abrir un hilo son UNA lectura puntual (más una escritura si muta). La
/// bandeja, en cambio, es un índice por participante: recorre el espacio
/// completo (un <c>ListAsync</c>) en cada llamada. No se cachea a propósito: en
/// este repo una caché se entrega junto con su invalidador y su consumidor,
/// nunca antes.</para>
///
/// <para><b>Ojo con los sembradores.</b> <see cref="StartThreadAsync"/> es
/// idempotente en el HILO, no en el MENSAJE: siempre agrega el cuerpo que
/// recibe. Con el store durable eso significa que cualquier siembra de demo debe
/// verificar antes si el hilo ya tiene mensajes — ver
/// <see cref="BlogsDemoSeeder"/>.</para>
/// </remarks>
public sealed class StubMessagingService : IMessagingService
{
    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-social-messages/).</summary>
    public const string DefaultResourceType = "social-messages";

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly Func<DateTimeOffset> _now;
    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write del append. lock{} no sirve: el cuerpo hace await.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubMessagingService()
        : this(null)
    {
    }

    /// <summary>Ctor con time source inyectable para determinismo en tests. Null = reloj real.</summary>
    public StubMessagingService(Func<DateTimeOffset>? now)
        : this(now, null, null)
    {
    }

    /// <summary>
    /// Ctor completo: <paramref name="store"/> es el backing store durable
    /// (FileSystem en Web, InMemory en tests) y <paramref name="storeNamespace"/>
    /// separa los hilos de las otras familias.
    /// </summary>
    /// <param name="now">Reloj inyectable para los tests. Null usa el del sistema.</param>
    /// <param name="store">Backing store durable. Null deja los hilos en memoria.</param>
    /// <param name="storeNamespace">Familia de entidades. Null usa
    ///   <see cref="DefaultResourceType"/>.</param>
    public StubMessagingService(Func<DateTimeOffset>? now, IJsonEntityStore? store, string? storeNamespace)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? DefaultResourceType : storeNamespace;
    }

    public async Task<MessageThread> StartThreadAsync(
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

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(threadId, cancellationToken).ConfigureAwait(false)
                ?? new ThreadState(
                    ThreadId: threadId,
                    ContextRef: context,
                    Participants: new[] { sender, recipient },
                    Messages: new List<ThreadMessage>(),
                    CreatedAt: _now());

            Append(state, sender, text);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return ToThread(state);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<MessageThread> ReplyAsync(
        string threadId,
        string from,
        string body,
        CancellationToken cancellationToken = default)
    {
        var sender = RequireParticipant(from, nameof(from));
        var text = RequireBody(body);

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = string.IsNullOrWhiteSpace(threadId)
                ? null
                : await LoadAsync(threadId.Trim(), cancellationToken).ConfigureAwait(false);
            if (state is null)
            {
                throw new ArgumentException("Hilo no encontrado.", nameof(threadId));
            }
            if (!state.Participants.Any(p => string.Equals(p, sender, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException(
                    $"'{sender}' no es participante del hilo {state.ThreadId}.", nameof(from));
            }

            Append(state, sender, text);
            await SaveAsync(state, cancellationToken).ConfigureAwait(false);
            return ToThread(state);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<MessageThread?> GetThreadAsync(string threadId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadId))
        {
            return null;
        }
        var state = await LoadAsync(threadId.Trim(), cancellationToken).ConfigureAwait(false);
        return state is null ? null : ToThread(state);
    }

    public async Task<IReadOnlyList<MessageThreadSummary>> GetInboxAsync(
        string participant,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(participant))
        {
            return Array.Empty<MessageThreadSummary>();
        }

        var who = participant.Trim();
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all
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
    }

    /// <summary>
    /// Lee UN hilo. Un documento ilegible se trata como inexistente en vez de
    /// reventar (para <see cref="ReplyAsync"/> eso es "hilo no encontrado", el
    /// mismo error que ya lanzaba).
    /// </summary>
    private async Task<ThreadState?> LoadAsync(string threadId, CancellationToken cancellationToken)
        => TryParse(await _store.ReadAsync(_resourceType, threadId, cancellationToken).ConfigureAwait(false));

    /// <summary>Lee TODOS los hilos, saltando lo corrupto — un hilo ilegible no puede tumbar la bandeja.</summary>
    private async Task<List<ThreadState>> LoadAllAsync(CancellationToken cancellationToken)
    {
        // OJO: ListAsync devuelve los DOCUMENTOS, no las claves.
        var raws = await _store.ListAsync(_resourceType, cancellationToken).ConfigureAwait(false);
        var threads = new List<ThreadState>(raws.Count);
        foreach (var json in raws)
        {
            var state = TryParse(json);
            if (state is not null)
            {
                threads.Add(state);
            }
        }
        return threads;
    }

    private static ThreadState? TryParse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var state = JsonSerializer.Deserialize<ThreadState>(json, _json);
            if (state is null || string.IsNullOrWhiteSpace(state.ThreadId))
            {
                return null;
            }
            return state.Messages is null || state.Participants is null ? null : state;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private Task SaveAsync(ThreadState state, CancellationToken cancellationToken)
        => _store.WriteAsync(
            _resourceType, state.ThreadId, JsonSerializer.Serialize(state, _json), cancellationToken);

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

    /// <summary>Documento persistido: UN hilo con todos sus mensajes.</summary>
    private sealed record ThreadState(
        string ThreadId,
        string ContextRef,
        IReadOnlyList<string> Participants,
        List<ThreadMessage> Messages,
        DateTimeOffset CreatedAt);
}
