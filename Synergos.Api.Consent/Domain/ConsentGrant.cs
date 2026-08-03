using Synergos.Core;

namespace Synergos.Api.Consent.Domain;

/// <summary>
/// El permiso que una persona dio para un propósito concreto.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Subject">Quién lo dio — opaco.</param>
/// <param name="Purpose">Para qué, en un vocabulario estable: <c>salud.agenda</c>.</param>
/// <param name="PolicyVersion">Qué texto aceptó. Sin esto, "dio consentimiento" no significa nada.</param>
/// <param name="GrantedAtUtc">Cuándo.</param>
/// <param name="ExpiresAtUtc">Hasta cuándo, si vence.</param>
/// <param name="RevokedAtUtc">Cuándo lo retiró, si lo retiró.</param>
/// <remarks>
/// <para><b>Un consentimiento por PROPÓSITO, no uno global.</b> "Acepto todo" no es
/// consentimiento informado: quien acepta que le agenden una cita no aceptó que le manden
/// publicidad. Un permiso global haría imposible responder qué autorizó exactamente, que es la
/// única pregunta que importa el día que alguien reclama.</para>
///
/// <para><b>Revocar NO borra.</b> Se marca. "Nunca lo dio" y "lo dio y lo retiró el martes" son
/// cosas distintas para quien audita y para quien tiene que justificar lo que hizo mientras el
/// permiso estaba vigente.</para>
/// </remarks>
public sealed record ConsentGrant(
    string Id,
    Ref Subject,
    string Purpose,
    string PolicyVersion,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset? RevokedAtUtc = null)
{
    /// <summary>Si vale en el instante dado.</summary>
    public bool IsActive(DateTimeOffset now)
        => RevokedAtUtc is null && (ExpiresAtUtc is null || now < ExpiresAtUtc);
}
