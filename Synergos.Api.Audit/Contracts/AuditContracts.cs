using Synergos.Api.Audit.Domain;

namespace Synergos.Api.Audit.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Registrar una acción en la bitácora.</summary>
public sealed record AppendEntryRequest(
    string? ActorKind,
    string? ActorId,
    IReadOnlyList<string>? ActorRoles,
    string? Action,
    string? TargetKind,
    string? TargetId,
    IReadOnlyDictionary<string, string>? Details);

/// <summary>Cómo sale una entrada.</summary>
public sealed record AuditEntryResponse(
    string Id,
    string ActorKind,
    string ActorId,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string TargetKind,
    string TargetId,
    DateTimeOffset AtUtc,
    IReadOnlyDictionary<string, string> Details)
{
    public static AuditEntryResponse From(AuditEntry e) => new(
        e.Id, e.Actor.Principal.Kind, e.Actor.Principal.Id, e.Actor.Roles.ToList(),
        e.Action, e.Target.Kind, e.Target.Id, e.AtUtc, e.Details);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
