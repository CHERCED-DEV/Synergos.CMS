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
/// <param name="GrantedWith">Con qué fuerza se afirmó la identidad de quien lo dio.</param>
/// <param name="RevokedWith">Con qué fuerza se afirmó la de quien lo retiró.</param>
/// <remarks>
/// <para><b>Un consentimiento por PROPÓSITO, no uno global.</b> "Acepto todo" no es
/// consentimiento informado: quien acepta que le agenden una cita no aceptó que le manden
/// publicidad. Un permiso global haría imposible responder qué autorizó exactamente, que es la
/// única pregunta que importa el día que alguien reclama.</para>
///
/// <para><b>Revocar NO borra.</b> Se marca. "Nunca lo dio" y "lo dio y lo retiró el martes" son
/// cosas distintas para quien audita y para quien tiene que justificar lo que hizo mientras el
/// permiso estaba vigente.</para>
///
/// <para><b>Y con qué se afirmó que era esa persona se guarda aparte de quién es</b> (HU #14,
/// rebanada 5). «Fulano consintió» no dice nada sin «y así se supo que era fulano»: el día que
/// alguien niegue haberlo dado, la diferencia entre un token verificado y la palabra del sitio es
/// la diferencia entre poder sostenerlo y no. Es lo mismo que <c>Api.Messaging</c> guarda del
/// acuse de un acto desde la HU #13.</para>
///
/// <para><b>Nulo significa «no consta», y es la verdad sobre los permisos anteriores a esta
/// rebanada</b> — no un valor por defecto. Rellenarlos con <c>CmsSession</c> sería inventar una
/// afirmación que nadie hizo, que es exactamente el defecto #42 con otro disfraz: el archivo
/// diría que se comprobó algo que no se comprobó.</para>
///
/// <para><b>Otorgar y revocar llevan la suya por separado</b>, porque no siempre las hace la
/// misma persona con la misma fuerza: un consentimiento dado en ventanilla y retirado desde el
/// portal son dos actos, y el registro tiene que poder decir cómo se identificó cada uno.</para>
/// </remarks>
public sealed record ConsentGrant(
    string Id,
    Ref Subject,
    string Purpose,
    string PolicyVersion,
    DateTimeOffset GrantedAtUtc,
    DateTimeOffset? ExpiresAtUtc = null,
    DateTimeOffset? RevokedAtUtc = null,
    IdentityAssertion? GrantedWith = null,
    IdentityAssertion? RevokedWith = null)
{
    /// <summary>Si vale en el instante dado.</summary>
    public bool IsActive(DateTimeOffset now)
        => RevokedAtUtc is null && (ExpiresAtUtc is null || now < ExpiresAtUtc);
}
