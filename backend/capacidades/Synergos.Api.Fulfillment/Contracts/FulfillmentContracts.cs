using Synergos.Api.Fulfillment.Domain;

namespace Synergos.Api.Fulfillment.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Una dirección tal como llega o sale.</summary>
public sealed record AddressDto(
    string? Line1 = null, string? Line2 = null, string? City = null, string? Region = null, string? PostalCode = null,
    string? Country = null, string? Contact = null);

/// <summary>Registrar un envío.</summary>
public sealed record CreateShipmentRequest(
    string? ForKind = null, string? ForId = null, AddressDto? To = null, string? Carrier = null, string? TrackingCode = null);

/// <summary>Reportar un evento de la transportadora.</summary>
public sealed record TrackRequest(string? Status = null, string? Note = null);

/// <summary>Un evento del rastro.</summary>
public sealed record TrackingEventResponse(string Status, string? Note, DateTimeOffset AtUtc);

/// <summary>Cómo sale un envío.</summary>
public sealed record ShipmentResponse(
    string Id, string ForKind, string ForId, AddressDto To, string Carrier, string? TrackingCode,
    string Status, IReadOnlyList<TrackingEventResponse> Events, DateTimeOffset CreatedAtUtc)
{
    public static ShipmentResponse From(Shipment s) => new(
        s.Id, s.For.Kind, s.For.Id,
        new AddressDto(s.To.Line1, s.To.Line2, s.To.City, s.To.Region, s.To.PostalCode, s.To.Country, s.To.Contact),
        s.Carrier, s.TrackingCode, s.Status.ToString(),
        s.Events.Select(e => new TrackingEventResponse(e.Status.ToString(), e.Note, e.AtUtc)).ToList(),
        s.CreatedAtUtc);
}

/// <summary>Una porción de una lista, con su total.</summary>
public sealed record PageResponse<T>(IReadOnlyList<T> Items, int Total, int Offset, bool HasMore);
