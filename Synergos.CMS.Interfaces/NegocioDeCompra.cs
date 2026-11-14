namespace Synergos.CMS.Interfaces;

/// <summary>
/// Si el sitio vende en línea, ya fusionado para un sitio (ADR 0137, 016-contenido-piezas).
/// </summary>
/// <param name="EnLinea">Si las fichas y las tarjetas del sitio llevan el botón del carrito y el encabezado su ícono.
/// Un inmueble se visita y un servicio de Booking se reserva: ninguno de los dos se echa al carrito.</param>
public sealed record NegocioDeCompra(bool EnLinea);
