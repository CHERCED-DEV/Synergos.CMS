using Modelo = Synergos.Api.Engagement.Domain;

namespace Synergos.Api.Engagement.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Registrar una opinión.</summary>
public sealed record AddEngagementRequest(
    string? ActorKind, string? ActorId, string? TargetKind, string? TargetId,
    string? Kind, string? Body, int? Rating, string? ReactionCode, string? ParentId);

/// <summary>Mostrar u ocultar.</summary>
public sealed record VisibilityRequest(bool? Visible);

/// <summary>Cómo sale una opinión.</summary>
public sealed record EngagementResponse(
    string Id, string ActorKind, string ActorId, string TargetKind, string TargetId,
    string Kind, string? Body, int? Rating, string? ReactionCode, string? ParentId,
    bool Visible, DateTimeOffset AtUtc)
{
    public static EngagementResponse From(Modelo.Engagement e) => new(
        e.Id, e.Actor.Kind, e.Actor.Id, e.Target.Kind, e.Target.Id,
        e.Kind.ToString(), e.Body, e.Rating, e.ReactionCode, e.ParentId, e.Visible, e.AtUtc);
}

/// <summary>El resumen de un objetivo.</summary>
public sealed record SummaryResponse(
    int Comments, int Favorites, int Ratings, decimal? AverageRating, IReadOnlyDictionary<string, int> Reactions)
{
    public static SummaryResponse From(Modelo.EngagementSummary s) => new(
        s.Comments, s.Favorites, s.Ratings, s.AverageRating, s.Reactions);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
