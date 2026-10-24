namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de el portal de viajes ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="BookingPath">Por dónde reserva el elemento si el despliegue lo DECLARA
/// (<see cref="CaminoDeCompra.Puerta"/> o <see cref="CaminoDeCompra.Directa"/>), ya normalizado; nulo si no lo
/// declara, y entonces decide la regla de <see cref="CaminoDeCompra.Decidir"/> (línea 006, P5).</param>
public sealed record NegocioDeTravelShell(string ApiBase, string? BookingPath = null);
