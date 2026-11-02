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
/// <para><b>Y de dónde lee</b> (<c>LecturasBase</c>, 014-despiece-tienda, I4): la base de las lecturas genéricas
/// (<c>/api/lecturas/tienda</c>), con forma neutra de vertical. Vacía, la tienda lee las rutas interinas de
/// <c>ApiBase</c>, como antes. Es del despliegue.</para>
///
/// <para><b>Y en qué hora dice sus fechas</b> (<c>ZonaHoraria</c>, la de <c>Synergos:Listados:ZonaHoraria</c>,
/// 004-tienda-compra etapa 3): el pedido y cada etapa del seguimiento llegan como instantes, y «Mis compras» los dice
/// en la hora del sitio y en es-CO, no en la de quien mira ni en ISO crudo. Es del despliegue.</para>
///
/// <para><b>Y qué parte de la tienda es esta página</b> (<c>Vista</c>, 014-despiece-tienda, I3): la elige el
/// editor en el bloque; vacía, el inicio, que es la tienda entera de antes. Las <c>Rutas</c> dicen en qué página
/// del sitio vive cada vista, y salen de cómo está compuesto el sitio, no del bloque: el elemento enlaza ahí, y
/// la vista que no tiene página se sigue abriendo dentro de ésta.</para>
///
/// <para><b>Y qué productos muestra</b> (<c>Filtros</c>, 014-despiece-tienda, I5): lo que el editor elige en «Qué
/// productos muestra», ya como filtros fijos del catálogo. Vacío, todo el catálogo; «ofertas», sólo las ofertas.</para>
/// </remarks>
// «seleccion» elige DATOS, qué productos salen, y no vocabulario: viaja ya como `Filtros` (#196, tanda D).
[ElementoSynHost("storefront", TipoDeColocable.Funcionalidad, Diccionario = ["Storefront.Purchase"], SelectoresDeDatos = ["seleccion"])]
public sealed record StorefrontProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string LecturasBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string PurchasePath,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ShippingCountry,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ZonaHoraria,
    [property: CampoSynHost(OrigenDelCampo.Decision)] string? Vista,
    [property: CampoSynHost(OrigenDelCampo.Decision)] IReadOnlyList<FiltroFijo> Filtros,
    [property: CampoSynHost(OrigenDelCampo.Sitio)] IReadOnlyList<RutaDeUnaVista> Rutas);
