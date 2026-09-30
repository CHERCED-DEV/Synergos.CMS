namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-hero-banner&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba la imagen como <c>mediaUrl</c> y el
/// destino del botón como <c>ctaUrl</c>; el elemento lee <c>media</c> y <c>ctaLink</c>. El respaldo
/// SSR salía con la foto y el botón y, al hidratar, el hero quedaba sin imagen y sin botón: sólo
/// el título y el subtítulo sobre el fondo de reserva.</para>
///
/// <para><b><c>media</c> es la URL absoluta del medio</b> y <c>mediaAlt</c> su texto alternativo
/// (<c>altDefault</c>); sin él el elemento la trata como decorativa. <b><c>ctaLabel</c></b> es el
/// texto del botón que escribe el editor y, si lo deja vacío, el texto del enlace: el elemento
/// sólo pinta el botón con los dos, y el respaldo SSR ya hacía esa misma elección.</para>
///
/// <para>La sección <c>Synhost.Hero</c> la usa el respaldo SSR (el nombre de la sección cuando el
/// editor deja el título vacío). <c>eyebrow</c>, <c>align</c>, <c>tone</c> y <c>height</c> los
/// acepta el elemento y no los autora el ElementType: quedan como atributos. El destino de
/// apertura del enlace no viaja: el elemento no lo pinta.</para>
/// </remarks>
[ElementoSynHost("hero-banner", TipoDeColocable.Pieza, Diccionario = ["Synhost.Hero"])]
public sealed record HeroBannerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Title,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subtitle,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Media,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? MediaAlt,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? CtaLabel,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? CtaLink);
