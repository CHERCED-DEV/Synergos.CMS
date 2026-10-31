namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-seller&gt;</c>, la consola del vendedor: ventas, publicaciones, mensajes y devoluciones.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Seller</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para>El subtítulo viaja y se pinta bajo el título de la consola, como en academy: la vista lo
/// mandaba y el elemento lo tiraba (D1, medido en los 11 bloques el 2026-10-02 y el 2026-10-03).</para>
///
/// <para><b>Y en qué hora dice sus fechas</b> (<c>ZonaHoraria</c>, la de <c>Synergos:Listados:ZonaHoraria</c>, como el
/// storefront): la bandeja y el seguimiento de los pedidos van en la hora del sitio, no en la del navegador ni en UTC
/// (013-seller-operaciones, T004). Los textos nuevos de la consola salen de la sección <c>Seller.Operaciones</c>
/// (ADR 0136).</para>
/// </remarks>
[ElementoSynHost("seller", TipoDeColocable.Funcionalidad, Diccionario = ["Seller.Operaciones"])]
public sealed record SellerProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ZonaHoraria);
