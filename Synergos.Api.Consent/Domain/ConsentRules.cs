using Synergos.Core;

namespace Synergos.Api.Consent.Domain;

/// <summary>Lo que el consentimiento rechaza <b>solo</b>.</summary>
public static class ConsentRules
{
    public const string CodePrefix = "consent";

    public const int MaxPurposeLength = 120;

    /// <summary>Si el otorgamiento que llega se puede registrar.</summary>
    public static Rejection? CheckGrant(string? purpose, string? policyVersion, DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(purpose) || purpose!.Length > MaxPurposeLength)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_purpose",
                $"El propósito es obligatorio y no pasa de {MaxPurposeLength} caracteres. Un consentimiento global no es consentimiento informado.");
        }
        if (string.IsNullOrWhiteSpace(policyVersion))
        {
            // Sin la versión del texto, "dio consentimiento" no dice a QUÉ. El día que la
            // política cambie no habrá manera de saber quién aceptó cuál.
            return Rejection.Invalid($"{CodePrefix}.policy_version_required",
                "Hace falta la versión del texto que se aceptó.");
        }
        return expiresAt is { } hasta && hasta <= now
            ? Rejection.Invalid($"{CodePrefix}.expires_in_the_past", "Un consentimiento que nace vencido no autoriza nada.")
            : null;
    }

    /// <summary>
    /// Traduce el estado del permiso a un rechazo, o <c>null</c> si vale.
    /// </summary>
    /// <remarks>
    /// Los tres "no" se distinguen a propósito: <b>nunca otorgado</b> se resuelve pidiéndolo,
    /// <b>vencido</b> renovándolo y <b>revocado</b> no se resuelve — hay que respetarlo. Un
    /// rechazo genérico dejaría al llamador sin saber cuál de las tres cosas hacer.
    /// </remarks>
    public static Rejection? CheckActive(ConsentGrant? grant, string purpose, DateTimeOffset now)
    {
        if (grant is null)
        {
            return Rejection.Forbidden($"{CodePrefix}.not_granted", $"No hay consentimiento para '{purpose}'.");
        }
        if (grant.RevokedAtUtc is not null)
        {
            return Rejection.Forbidden($"{CodePrefix}.revoked",
                $"El consentimiento para '{purpose}' se retiró en {grant.RevokedAtUtc:O}.");
        }
        return grant.ExpiresAtUtc is { } hasta && now >= hasta
            ? Rejection.Expired($"{CodePrefix}.expired", $"El consentimiento para '{purpose}' venció en {hasta:O}.")
            : null;
    }
}
