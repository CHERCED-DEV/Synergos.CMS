using Synergos.Api.Workflow.Domain;

namespace Synergos.Api.Workflow.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Una transición tal como llega.</summary>
public sealed record TransitionDto(string? Name, string? From, string? To, IReadOnlyList<string>? RequiredRoles);

/// <summary>Publicar una definición.</summary>
public sealed record DefineRequest(
    string? Key, string? InitialState, IReadOnlyList<string>? FinalStates, IReadOnlyList<TransitionDto>? Transitions);

/// <summary>Arrancar una instancia.</summary>
public sealed record StartRequest(string? DefinitionKey, string? SubjectKind, string? SubjectId);

/// <summary>Disparar una transición.</summary>
public sealed record FireRequest(
    string? Transition, string? ActorKind, string? ActorId, IReadOnlyList<string>? ActorRoles, string? Note);

/// <summary>Cómo sale una definición.</summary>
public sealed record DefinitionResponse(
    string Id, string Key, string InitialState, IReadOnlyList<string> FinalStates,
    IReadOnlyList<string> States, IReadOnlyList<TransitionDto> Transitions)
{
    public static DefinitionResponse From(WorkflowDefinition d) => new(
        d.Id, d.Key, d.InitialState, d.FinalStates,
        d.States.OrderBy(s => s, StringComparer.Ordinal).ToList(),
        d.Transitions.Select(t => new TransitionDto(t.Name, t.From, t.To, t.RequiredRoles)).ToList());
}

/// <summary>Un paso de la historia.</summary>
public sealed record HistoryResponse(string Transition, string From, string To, string ByKind, string ById, string? Note, DateTimeOffset AtUtc);

/// <summary>Cómo sale una instancia.</summary>
public sealed record InstanceResponse(
    string Id, string DefinitionKey, string SubjectKind, string SubjectId, string State,
    IReadOnlyList<HistoryResponse> History, DateTimeOffset StartedAtUtc)
{
    public static InstanceResponse From(WorkflowInstance i) => new(
        i.Id, i.DefinitionKey, i.Subject.Kind, i.Subject.Id, i.State,
        i.History.Select(h => new HistoryResponse(h.Transition, h.From, h.To, h.By.Kind, h.By.Id, h.Note, h.AtUtc)).ToList(),
        i.StartedAtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
