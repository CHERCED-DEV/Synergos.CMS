namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-storefront&gt;</c>, la tienda: catálogo, ficha, carrito y checkout.
/// </summary>
/// <remarks>
/// <para><b>Es una FUNCIONALIDAD</b> (ADR 0134): del editor recibe sólo su contenido; dónde vive su
/// API sale de <c>Synergos:Features:Storefront</c>, por sitio (ADR 0137, escala #196). El JSON libre
/// del editor no viaja: medido el 2026-10-02, ningún bloque lo usaba.</para>
///
/// <para><b>Y por dónde compra</b> (<c>PurchasePath</c>, ADR 0140): la puerta (<c>tienda.compra</c>) o la
/// ruta directa <c>/api/shop/checkout|confirm</c>, que se conservan las dos. Lo DECLARA el CMS con la regla
/// del camino de los flujos —el override de <c>Synergos:Features:Storefront:PurchasePath</c>, el del flujo,
/// o la puerta si abre—, y el elemento nunca cae de un camino al otro. Por la puerta, la dirección lleva el
/// país del despliegue (<c>ShippingCountry</c>), y los textos de la compra salen de la sección
/// <c>Storefront.Purchase</c> del diccionario (ADR 0136).</para>
///
/// <para><b>Y en qué hora dice sus fechas</b> (<c>ZonaHoraria</c>, la de <c>Synergos:Listados:ZonaHoraria</c>,
/// 004-tienda-compra etapa 3): el pedido y cada etapa del seguimiento llegan como instantes, y «Mis compras» los dice
/// en la hora del sitio y en es-CO, no en la de quien mira ni en ISO crudo. Es del despliegue.</para>
/// </remarks>
[ElementoSynHost("storefront", TipoDeColocable.Funcionalidad, Diccionario = ["Storefront.Purchase"])]
public sealed record StorefrontProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string PurchasePath,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ShippingCountry,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ZonaHoraria);
