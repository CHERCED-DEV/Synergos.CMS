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
/// de <see cref="CaminoDeCompra"/> —el override de <c>Synergos:Features:Storefront:PurchasePath</c>, o la
/// puerta si abre el flujo, o la directa—, y el elemento nunca cae de un camino al otro.</para>
/// </remarks>
[ElementoSynHost("storefront", TipoDeColocable.Funcionalidad)]
public sealed record StorefrontProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Heading,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Subheading,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string ApiBase,
    [property: CampoSynHost(OrigenDelCampo.Negocio)] string PurchasePath);
