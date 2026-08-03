using Synergos.Core;

namespace Synergos.Api.Fulfillment.Domain;

/// <summary>Lo que el envío rechaza <b>solo</b>.</summary>
public static class FulfillmentRules
{
    public const string CodePrefix = "fulfillment";

    /// <summary>Las transiciones que la transportadora puede reportar.</summary>
    /// <remarks>
    /// Un envío entregado no vuelve a tránsito, y uno devuelto no se entrega: los estados
    /// finales son finales. Sin esto, un evento tardío de la transportadora —llegan
    /// desordenados— haría retroceder el estado y el cliente vería "en tránsito" un paquete que
    /// ya tiene en la mano.
    /// </remarks>
    private static readonly IReadOnlyDictionary<ShipmentStatus, ShipmentStatus[]> Permitidas =
        new Dictionary<ShipmentStatus, ShipmentStatus[]>
        {
            [ShipmentStatus.Ready] = new[] { ShipmentStatus.InTransit, ShipmentStatus.Failed },
            [ShipmentStatus.InTransit] = new[] { ShipmentStatus.Delivered, ShipmentStatus.Failed, ShipmentStatus.Returned },
        };

    /// <summary>Si la dirección alcanza para que alguien la entregue.</summary>
    public static Rejection? CheckAddress(Address? to)
    {
        if (to is null) return Rejection.Invalid($"{CodePrefix}.address_required", "Hace falta una dirección.");
        if (string.IsNullOrWhiteSpace(to.Line1)) return Rejection.Invalid($"{CodePrefix}.address_line1_required", "Hace falta la calle.");
        if (string.IsNullOrWhiteSpace(to.City)) return Rejection.Invalid($"{CodePrefix}.address_city_required", "Hace falta la ciudad.");

        return string.IsNullOrWhiteSpace(to.Country) || to.Country.Trim().Length != 2
            ? Rejection.Invalid($"{CodePrefix}.address_country_required", "Hace falta el país en ISO 3166-1 alfa-2.")
            : null;
    }

    /// <summary>Si el envío puede pasar a ese estado.</summary>
    public static Rejection? CheckTransition(Shipment shipment, ShipmentStatus destino)
    {
        if (shipment.Status == destino)
        {
            return Rejection.Conflict($"{CodePrefix}.already_{destino.ToString().ToLowerInvariant()}",
                $"El envío ya estaba {destino}.");
        }
        if (shipment.IsClosed)
        {
            return Rejection.Conflict($"{CodePrefix}.shipment_closed",
                $"El envío está {shipment.Status}, que es final. Un evento tardío no lo hace retroceder.");
        }
        return Permitidas.TryGetValue(shipment.Status, out var posibles) && posibles.Contains(destino)
            ? null
            : Rejection.Conflict($"{CodePrefix}.transition_not_allowed",
                $"De {shipment.Status} no se pasa a {destino}.");
    }
}
