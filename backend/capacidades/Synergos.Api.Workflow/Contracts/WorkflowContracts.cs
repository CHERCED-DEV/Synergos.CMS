using Synergos.Api.Workflow.Domain;

namespace Synergos.Api.Workflow.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).
//
// Los campos de las peticiones son anulables CON `= null` (009-P2, el suelo del contrato publicado): un campo
// ausente ya llegaba como null y lo rechaza el borde con su código; sin el default, el documento los publicaba
// como requeridos y un generador de tipos obligaría a mandar lo que la capacidad acepta que falte.

/// <summary>Una transición tal como llega.</summary>
public sealed record TransitionDto(
    string? Name = null, string? From = null, string? To = null, IReadOnlyList<string>? RequiredRoles = null);

/// <summary>Publicar una definición.</summary>
public sealed record DefineRequest(
    string? Key = null, string? InitialState = null, IReadOnlyList<string>? FinalStates = null,
    IReadOnlyList<TransitionDto>? Transitions = null);

/// <summary>Arrancar una instancia.</summary>
public sealed record StartRequest(string? DefinitionKey = null, string? SubjectKind = null, string? SubjectId = null);

/// <summary>Disparar una transición.</summary>
public sealed record FireRequest(
    string? Transition = null, string? ActorKind = null, string? ActorId = null,
    IReadOnlyList<string>? ActorRoles = null, string? Note = null);

/// <summary>Cómo sale una definición.</summary>
public sealed record DefinitionResponse(
    string Id, string Key, string InitialState, IReadOnlyList<string> FinalStates,
    IReadOnlyList<string> States, IReadOnlyList<TransitionResponse> Transitions)
{
    public static DefinitionResponse From(WorkflowDefinition d) => new(
        d.Id, d.Key, d.InitialState, d.FinalStates,
        d.States.OrderBy(s => s, StringComparer.Ordinal).ToList(),
        d.Transitions.Select(t => new TransitionResponse(t.Name, t.From, t.To, t.RequiredRoles)).ToList());
}

/// <summary>Cómo sale una transición.</summary>
public sealed record TransitionResponse(string Name, string From, string To, IReadOnlyList<string> RequiredRoles);

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
