using Synergos.Api.Fulfillment.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Fulfillment.Domain;

/// <summary>Compone las reglas de <see cref="FulfillmentRules"/> con el almacén.</summary>
public sealed class FulfillmentService
{
    private readonly IShipmentStore _shipments;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public FulfillmentService(IShipmentStore shipments, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _shipments = shipments;
        _idempotency = idempotency;
        _clock = clock;
    }

    public Result<Shipment> Create(Ref forWhat, Address? to, string? carrier, string? trackingCode, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("shipment", key) is { } yaEra)
            {
                return _shipments.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{FulfillmentRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el envío no está.");
            }

            var motivo = FulfillmentRules.CheckAddress(to);
            if (motivo is not null) return Result.Rejected<Shipment>(motivo);

            if (string.IsNullOrWhiteSpace(carrier))
            {
                return Rejection.Invalid($"{FulfillmentRules.CodePrefix}.carrier_required", "Hace falta la transportadora.");
            }
            if (!string.IsNullOrWhiteSpace(trackingCode) && _shipments.FindByTracking(trackingCode!) is not null)
            {
                // Dos envíos con el mismo número harían que seguir el paquete devolviera uno u
                // otro según el orden del almacén.
                return Rejection.Conflict($"{FulfillmentRules.CodePrefix}.tracking_taken",
                    $"Ya hay un envío con el número '{trackingCode}'.");
            }

            var ahora = _clock.GetUtcNow();
            var id = Guid.NewGuid().ToString("n");
            var envio = new Shipment(id, forWhat, to!, carrier!.Trim(), trackingCode?.Trim(),
                ShipmentStatus.Ready, new[] { new TrackingEvent(ShipmentStatus.Ready, null, ahora) }, ahora);

            _shipments.Put(envio);
            _idempotency.Remember("shipment", key, id);
            return Result.Ok(envio);
        }
    }

    public Result<Shipment> Get(string id)
        => _shipments.Find(id) is { } s
            ? Result.Ok(s)
            : Rejection.NotFound($"{FulfillmentRules.CodePrefix}.shipment_not_found", $"No existe el envío {id}.");

    /// <summary>Registra un evento de la transportadora.</summary>
    public Result<Shipment> Track(string id, ShipmentStatus status, string? note)
    {
        lock (_gate)
        {
            var envio = _shipments.Find(id);
            if (envio is null)
            {
                return Rejection.NotFound($"{FulfillmentRules.CodePrefix}.shipment_not_found", $"No existe el envío {id}.");
            }

            var motivo = FulfillmentRules.CheckTransition(envio, status);
            if (motivo is not null) return Result.Rejected<Shipment>(motivo);

            var evento = new TrackingEvent(status, note?.Trim(), _clock.GetUtcNow());
            var actualizado = envio with { Status = status, Events = envio.Events.Append(evento).ToList() };

            _shipments.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    public Result<Page<Shipment>> ListForSubject(Ref? subject, int offset, int limit)
    {
        if (subject is null)
        {
            return Rejection.Invalid($"{FulfillmentRules.CodePrefix}.subject_required", "Hace falta filtrar por aquello que se envía.");
        }

        var todos = _shipments.ForSubject(subject)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Shipment>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }
}
