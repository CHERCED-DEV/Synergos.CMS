using Synergos.Api.Moderation.Domain;

namespace Synergos.Api.Moderation.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Reportar algo.</summary>
public sealed record ReportRequest(
    string? TargetKind, string? TargetId, string? ReporterKind, string? ReporterId, string? Reason);

/// <summary>Resolver un caso.</summary>
public sealed record DecideRequest(string? Decision, string? DecidedByKind, string? DecidedById, string? Note);

/// <summary>Cómo sale un caso.</summary>
public sealed record CaseResponse(
    string Id, string TargetKind, string TargetId, string? ReporterKind, string? ReporterId,
    string Reason, string Status, string? Decision, string? DecidedByKind, string? DecidedById,
    string? DecisionNote, DateTimeOffset ReportedAtUtc, DateTimeOffset? DecidedAtUtc)
{
    public static CaseResponse From(ModerationCase c) => new(
        c.Id, c.Target.Kind, c.Target.Id, c.Reporter?.Kind, c.Reporter?.Id,
        c.Reason, c.Status.ToString(), c.Decision?.ToString(), c.DecidedBy?.Kind, c.DecidedBy?.Id,
        c.DecisionNote, c.ReportedAtUtc, c.DecidedAtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
