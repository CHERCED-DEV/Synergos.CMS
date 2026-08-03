using Synergos.Api.Messaging.Domain;

namespace Synergos.Api.Messaging.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Una referencia tal como llega o sale.</summary>
public sealed record RefDto(string? Kind, string? Id);

/// <summary>Abrir un hilo.</summary>
public sealed record OpenThreadRequest(string? TopicKind, string? TopicId, IReadOnlyList<RefDto>? Participants);

/// <summary>Escribir en un hilo.</summary>
public sealed record PostRequest(string? FromKind, string? FromId, string? Body, IReadOnlyList<RefDto>? Attachments);

/// <summary>Marcar como leído.</summary>
public sealed record MarkReadRequest(string? WhoKind, string? WhoId);

/// <summary>Cómo sale un hilo.</summary>
public sealed record ThreadResponse(
    string Id, string TopicKind, string TopicId, IReadOnlyList<RefDto> Participants, bool Closed, DateTimeOffset OpenedAtUtc)
{
    public static ThreadResponse From(MessageThread t) => new(
        t.Id, t.Topic.Kind, t.Topic.Id,
        t.Participants.Select(p => new RefDto(p.Kind, p.Id)).ToList(), t.Closed, t.OpenedAtUtc);
}

/// <summary>Cómo sale un mensaje.</summary>
public sealed record MessageResponse(
    string Id, string ThreadId, string FromKind, string FromId, string Body,
    IReadOnlyList<RefDto> Attachments, IReadOnlyList<RefDto> ReadBy, DateTimeOffset AtUtc)
{
    public static MessageResponse From(Message m) => new(
        m.Id, m.ThreadId, m.From.Kind, m.From.Id, m.Body,
        m.Attachments.Select(a => new RefDto(a.Kind, a.Id)).ToList(),
        m.ReadBy.Select(r => new RefDto(r.Kind, r.Id)).ToList(), m.AtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
