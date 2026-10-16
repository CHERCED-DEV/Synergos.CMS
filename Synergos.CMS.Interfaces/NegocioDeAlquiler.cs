namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio del alquiler de equipos ya fusionados para un sitio (ADR 0137, #147).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="RentalPath">Por dónde alquila el elemento si el despliegue lo DECLARA
/// (<see cref="CaminoDeCompra.Puerta"/> o <see cref="CaminoDeCompra.Directa"/>), ya normalizado; nulo si no lo
/// declara, y entonces decide la regla de <see cref="CaminoDeCompra.Decidir"/> (línea 005, research R9).</param>
public sealed record NegocioDeAlquiler(string ApiBase, string? RentalPath = null);
