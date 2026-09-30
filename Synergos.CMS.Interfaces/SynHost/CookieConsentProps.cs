namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-cookie-consent&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba el enlace a la política como
/// <c>policyUrl</c> y el elemento lee <c>policyLink</c>: el aviso de cookies colocado salía sin
/// enlace a la política de privacidad, que es lo que lo hace un consentimiento informado.</para>
///
/// <para><b><c>policyLink</c> es el destino del enlace</b> y <b><c>policyLabel</c></b> el texto
/// que el editor escribió para él (sin texto, el elemento dice «Política de cookies»). Dónde abre
/// no viaja: el elemento abre la política siempre en una pestaña nueva, para no sacar al
/// visitante de una decisión a medias.</para>
///
/// <para><c>title</c>, <c>saveLabel</c>, <c>storageKey</c> y <c>categories</c> los acepta el
/// elemento y no los autora el ElementType: quedan como atributos. <c>storageKey</c> es un ajuste
/// técnico y <c>categories</c> la lista de categorías de cookies, que es del sitio y no de cada
/// bloque.</para>
/// </remarks>
[ElementoSynHost("cookie-consent", TipoDeColocable.Pieza)]
public sealed record CookieConsentProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? BannerText,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? AcceptLabel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? RejectLabel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? SettingsLabel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? PolicyLink,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? PolicyLabel);
