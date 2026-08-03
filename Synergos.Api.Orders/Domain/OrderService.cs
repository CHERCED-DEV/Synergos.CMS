using Synergos.Api.Orders.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Orders.Domain;

/// <summary>Compone las reglas de <see cref="OrderRules"/> con el almacén.</summary>
public sealed class OrderService
{
    private readonly IOrderStore _orders;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public OrderService(IOrderStore orders, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _orders = orders;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<Order> Place(Ref buyer, IReadOnlyList<OrderLine> lines, Money total, IdempotencyKey key)
    {
        lock (_gate)
        {
            // La llave primero: un reintento tras un timeout tiene que devolver el pedido ya
            // registrado. Sin esto, el cliente termina con dos pedidos de la misma compra — y
            // acá eso son dos cobros.
            if (_idempotency.Find("order", key) is { } yaEra)
            {
                return _orders.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{OrderRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el pedido no está.");
            }

            var motivo = OrderRules.CheckLines(lines) ?? OrderRules.CheckTotal(lines, total);
            if (motivo is not null) return Result.Rejected<Order>(motivo);

            var id = Guid.NewGuid().ToString("n");
            var order = new Order(id, buyer, lines, total, OrderStatus.Placed, Now);
            _orders.Put(order);
            _idempotency.Remember("order", key, id);
            return Result.Ok(order);
        }
    }

    public Result<Order> Get(string id)
        => _orders.Find(id) is { } o
            ? Result.Ok(o)
            : Rejection.NotFound($"{OrderRules.CodePrefix}.order_not_found", $"No existe el pedido {id}.");

    public Result<Order> Fulfill(string id) => Transition(id, OrderStatus.Fulfilled);

    public Result<Order> Cancel(string id) => Transition(id, OrderStatus.Cancelled);

    private Result<Order> Transition(string id, OrderStatus destino)
    {
        lock (_gate)
        {
            var order = _orders.Find(id);
            if (order is null)
            {
                return Rejection.NotFound($"{OrderRules.CodePrefix}.order_not_found", $"No existe el pedido {id}.");
            }

            var motivo = OrderRules.CheckTransition(order, destino);
            if (motivo is not null) return Result.Rejected<Order>(motivo);

            var actualizado = order with { Status = destino, ClosedAtUtc = Now };
            _orders.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    public Result<Page<Order>> ListForBuyer(Ref? buyer, int offset, int limit)
    {
        if (buyer is null)
        {
            return Rejection.Invalid($"{OrderRules.CodePrefix}.buyer_required",
                "Hace falta filtrar por comprador: sin filtro esto es un volcado del almacén.");
        }

        var todos = _orders.ForBuyer(buyer)
            .OrderByDescending(o => o.PlacedAtUtc)
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Order>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }
}
