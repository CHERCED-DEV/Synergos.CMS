using Synergos.Api.Audit.Domain;

namespace Synergos.Api.Audit.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Registrar una acción en la bitácora.</summary>
/// <param name="ActorRoles">
/// Los roles que declara quien llama. <b>Se usan SOLO si no presenta token</b>: con token, los
/// que valen son los que vienen firmados dentro (#72, la lección de #48).
/// </param>
/// <param name="Assertion">
/// Con qué dice quien llama que se afirmó la identidad del actor. Lo único que se acepta sin
/// prueba es <c>CmsSession</c>; declarar algo más fuerte sin presentarlo se rechaza.
/// </param>
public sealed record AppendEntryRequest(
    string? ActorKind,
    string? ActorId,
    IReadOnlyList<string>? ActorRoles,
    string? Action,
    string? TargetKind,
    string? TargetId,
    IReadOnlyDictionary<string, string>? Details,
    string? Assertion = null);

/// <summary>Cómo sale una entrada.</summary>
/// <param name="ActedWith">
/// Con qué se afirmó la identidad del actor, o <c>null</c> si no consta — los asientos anteriores
/// a #72. Sale a propósito: sin esto, quien consulta la bitácora no puede distinguir un asiento
/// respaldado por un token de uno que sólo lleva la palabra de quien lo escribió.
/// </param>
public sealed record AuditEntryResponse(
    string Id,
    string ActorKind,
    string ActorId,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string TargetKind,
    string TargetId,
    DateTimeOffset AtUtc,
    IReadOnlyDictionary<string, string> Details,
    string? ActedWith)
{
    public static AuditEntryResponse From(AuditEntry e) => new(
        e.Id, e.Actor.Principal.Kind, e.Actor.Principal.Id, e.Actor.Roles.ToList(),
        e.Action, e.Target.Kind, e.Target.Id, e.AtUtc, e.Details, e.ActedWith?.ToString());
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
