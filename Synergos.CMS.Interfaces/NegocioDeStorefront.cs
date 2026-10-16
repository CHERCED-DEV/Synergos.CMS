namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de la tienda ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="Carrier">La transportadora con que se despacha una compra por la puerta. La pone la puerta en
/// <c>X-Synergos-Negocio</c> al abrir <c>tienda.compra</c>: del navegador no viaja.</param>
/// <param name="PurchasePath">Por dónde compra la tienda si el despliegue lo DECLARA
/// (<see cref="CaminoDeCompra.Puerta"/> o <see cref="CaminoDeCompra.Directa"/>), ya normalizado; nulo si no lo
/// declara, y entonces decide la regla de <see cref="CaminoDeCompra.Decidir"/>.</param>
public sealed record NegocioDeStorefront(string ApiBase, string Carrier = "default", string? PurchasePath = null);
