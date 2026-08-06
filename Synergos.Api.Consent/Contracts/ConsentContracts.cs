using Synergos.Api.Consent.Domain;

namespace Synergos.Api.Consent.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Otorgar consentimiento.</summary>
public sealed record GrantRequest(
    string? SubjectKind, string? SubjectId, string? Purpose, string? PolicyVersion, DateTimeOffset? ExpiresAtUtc);

/// <summary>Retirar consentimiento.</summary>
public sealed record RevokeRequest(string? SubjectKind, string? SubjectId, string? Purpose);

/// <summary>Derecho al olvido.</summary>
public sealed record ForgetRequest(string? SubjectKind, string? SubjectId);

/// <summary>Cómo sale un consentimiento.</summary>
public sealed record ConsentResponse(
    string Id, string SubjectKind, string SubjectId, string Purpose, string PolicyVersion,
    DateTimeOffset GrantedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RevokedAtUtc, bool Active)
{
    public static ConsentResponse From(ConsentGrant g, DateTimeOffset now) => new(
        g.Id, g.Subject.Kind, g.Subject.Id, g.Purpose, g.PolicyVersion,
        g.GrantedAtUtc, g.ExpiresAtUtc, g.RevokedAtUtc, g.IsActive(now));
}

/// <summary>Cuántos permisos tocó el olvido.</summary>
public sealed record ForgetResponse(int Revoked);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
