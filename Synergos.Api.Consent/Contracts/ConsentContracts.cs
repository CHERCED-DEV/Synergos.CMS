using Synergos.Api.Consent.Domain;

namespace Synergos.Api.Consent.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Otorgar consentimiento.</summary>
/// <param name="Assertion">
/// Con qué se afirma la identidad de quien lo da. <b>Sólo se acepta a la baja</b>: declarar algo
/// más fuerte que <c>CmsSession</c> sin presentar el token se rechaza (HU #14).
/// </param>
public sealed record GrantRequest(
    string? SubjectKind, string? SubjectId, string? Purpose, string? PolicyVersion,
    DateTimeOffset? ExpiresAtUtc, string? Assertion);

/// <summary>Retirar consentimiento — y consultar, que usa la misma forma.</summary>
public sealed record RevokeRequest(string? SubjectKind, string? SubjectId, string? Purpose, string? Assertion);

/// <summary>Derecho al olvido.</summary>
/// <param name="Assertion">
/// Con qué dice quien llama que se afirmó la identidad del sujeto. <b>Obligatorio</b>, igual que
/// en <c>revoke</c>: retirarle TODOS los permisos a alguien es más grave que retirarle uno, y
/// hasta el defecto #83 era lo único que no pedía nada (defecto #83).
/// </param>
public sealed record ForgetRequest(string? SubjectKind, string? SubjectId, string? Assertion = null);

/// <summary>Cómo sale un consentimiento.</summary>
/// <param name="GrantedWith">
/// Con qué se afirmó la identidad de quien lo dio. <b>Nulo es «no consta»</b> — lo que dicen los
/// permisos anteriores a la HU #14 rebanada 5, y es la verdad sobre ellos.
/// </param>
/// <param name="RevokedWith">Ídem para quien lo retiró.</param>
public sealed record ConsentResponse(
    string Id, string SubjectKind, string SubjectId, string Purpose, string PolicyVersion,
    DateTimeOffset GrantedAtUtc, DateTimeOffset? ExpiresAtUtc, DateTimeOffset? RevokedAtUtc, bool Active,
    string? GrantedWith, string? RevokedWith)
{
    public static ConsentResponse From(ConsentGrant g, DateTimeOffset now) => new(
        g.Id, g.Subject.Kind, g.Subject.Id, g.Purpose, g.PolicyVersion,
        g.GrantedAtUtc, g.ExpiresAtUtc, g.RevokedAtUtc, g.IsActive(now),
        g.GrantedWith?.ToString(), g.RevokedWith?.ToString());
}

/// <summary>Cuántos permisos tocó el olvido.</summary>
public sealed record ForgetResponse(int Revoked);

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
