using Synergos.Api.Identity.Domain;
using Synergos.Core;

namespace Synergos.Api.Identity.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1). Acá la separación gana algo
// concreto: Principal lleva la credencial derivada y el contador de fallos, y NINGUNO de los
// dos puede salir. Con un solo tipo, ese "no lo serialices" sería un comentario.

/// <summary>Registrar un principal.</summary>
public sealed record RegisterPrincipalRequest(
    string? SubjectKind, string? SubjectId, string? Secret, IReadOnlyList<string>? Roles);

/// <summary>Verificar una credencial.</summary>
public sealed record VerifyCredentialRequest(string? SubjectKind, string? SubjectId, string? Secret);

/// <summary>Otorgar o revocar roles.</summary>
public sealed record RolesRequest(IReadOnlyList<string>? Roles);

/// <summary>Cómo sale un principal. <b>Sin la credencial y sin el contador de fallos.</b></summary>
public sealed record PrincipalResponse(
    string Id, string SubjectKind, string SubjectId, IReadOnlyList<string> Roles,
    bool Locked, DateTimeOffset? LockedUntilUtc)
{
    public static PrincipalResponse From(Principal p, DateTimeOffset now) => new(
        p.Id, p.Subject.Kind, p.Subject.Id, p.Roles, p.IsLocked(now), p.LockedUntilUtc);
}

/// <summary>Quién resultó ser, para el que autoriza y el que audita.</summary>
public sealed record ActorResponse(string PrincipalKind, string PrincipalId, IReadOnlyList<string> Roles)
{
    public static ActorResponse From(Actor a) => new(a.Principal.Kind, a.Principal.Id, a.Roles.ToList());
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
