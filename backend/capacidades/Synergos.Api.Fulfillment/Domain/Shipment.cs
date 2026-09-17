using Synergos.Core;

namespace Synergos.Api.Fulfillment.Domain;

/// <summary>Dónde entregar.</summary>
/// <param name="Line1">Calle y número.</param>
/// <param name="Line2">Apartamento, torre, indicaciones.</param>
/// <param name="City">Ciudad.</param>
/// <param name="Region">Departamento o estado.</param>
/// <param name="PostalCode">Código postal, si el país lo usa.</param>
/// <param name="Country">País, ISO 3166-1 alfa-2.</param>
/// <param name="Contact">Teléfono o correo de quien recibe.</param>
/// <remarks>
/// <b>El código postal es opcional a propósito.</b> En Colombia mucha gente no lo sabe ni lo
/// usa, y exigirlo bloquearía entregas perfectamente posibles. Lo que sí se exige es ciudad y
/// país: sin eso no hay transportadora que pueda cotizar.
/// </remarks>
public sealed record Address(
    string Line1, string? Line2, string City, string? Region, string? PostalCode, string Country, string? Contact);

/// <summary>En qué punto está un envío.</summary>
/// <remarks>
/// <b>Los estados son los de la transportadora, no los del pedido.</b> Un pedido "cumplido" y un
/// envío "entregado" son cosas distintas: un servicio prestado en sitio cumple el pedido sin que
/// exista envío. Por eso esto es una capacidad aparte de <c>Api.Orders</c>.
/// </remarks>
public enum ShipmentStatus
{
    /// <summary>Registrado, todavía sin recoger.</summary>
    Ready,

    /// <summary>La transportadora lo tiene.</summary>
    InTransit,

    /// <summary>Llegó. Estado final.</summary>
    Delivered,

    /// <summary>No se pudo entregar. Estado final.</summary>
    Failed,

    /// <summary>Vuelve al origen. Estado final.</summary>
    Returned,
}

/// <summary>Un cambio de estado, con lo que dijo la transportadora.</summary>
public sealed record TrackingEvent(ShipmentStatus Status, string? Note, DateTimeOffset AtUtc);

/// <summary>Un envío y su rastro.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="For">De qué es el envío — opaco. Normalmente un pedido.</param>
/// <param name="To">A dónde va.</param>
/// <param name="Carrier">Quién lo lleva.</param>
/// <param name="TrackingCode">Con qué número lo sigue el destinatario.</param>
/// <param name="Status">En qué punto está.</param>
/// <param name="Events">Cómo llegó hasta acá.</param>
/// <param name="CreatedAtUtc">Cuándo se registró.</param>
public sealed record Shipment(
    string Id, Ref For, Address To, string Carrier, string? TrackingCode,
    ShipmentStatus Status, IReadOnlyList<TrackingEvent> Events, DateTimeOffset CreatedAtUtc)
{
    public bool IsClosed => Status is ShipmentStatus.Delivered or ShipmentStatus.Failed or ShipmentStatus.Returned;
}
