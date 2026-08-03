using Synergos.Api.Inventory.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Inventory.Domain;

/// <summary>Compone las reglas de <see cref="InventoryRules"/> con el almacén.</summary>
public sealed class InventoryService
{
    private readonly IStockStore _stock;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public InventoryService(IStockStore stock, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _stock = stock;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<StockItem> Declare(Ref subject, int onHand, IReadOnlyList<StockUnit> units, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("stock", key) is { } yaEra)
            {
                return _stock.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{InventoryRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el ítem no está.");
            }

            if (onHand < 0)
            {
                return Rejection.Invalid($"{InventoryRules.CodePrefix}.negative_stock", "Las existencias no pueden ser negativas.");
            }
            if (units.Count > 0 && units.Count != onHand)
            {
                // Si hay unidades nombradas, TIENE que haber tantas como existencias: si no, no
                // hay manera de saber cuáles son las que faltan cuando se agoten.
                return Rejection.Invalid($"{InventoryRules.CodePrefix}.units_mismatch",
                    $"Se declararon {units.Count} unidades nombradas y {onHand} existencias. Tienen que coincidir.");
            }
            if (units.Select(u => u.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count() != units.Count)
            {
                return Rejection.Invalid($"{InventoryRules.CodePrefix}.duplicate_units", "Hay códigos de unidad repetidos.");
            }
            if (_stock.FindBySubject(subject) is not null)
            {
                return Rejection.Conflict($"{InventoryRules.CodePrefix}.subject_taken",
                    $"Ya hay existencias declaradas para {subject}. Para cambiarlas está /adjust.");
            }

            var id = Guid.NewGuid().ToString("n");
            var item = new StockItem(id, subject, onHand, units, Array.Empty<StockHold>());
            _stock.Put(item);
            _idempotency.Remember("stock", key, id);
            return Result.Ok(item);
        }
    }

    public Result<StockItem> Get(string id)
        => _stock.Find(id) is { } i
            ? Result.Ok(i)
            : Rejection.NotFound($"{InventoryRules.CodePrefix}.item_not_found", $"No existe el ítem {id}.");

    public Result<StockItem> Adjust(string id, int nuevoTotal)
    {
        lock (_gate)
        {
            var item = _stock.Find(id);
            if (item is null)
            {
                return Rejection.NotFound($"{InventoryRules.CodePrefix}.item_not_found", $"No existe el ítem {id}.");
            }
            if (item.HasNamedUnits)
            {
                return Rejection.Conflict($"{InventoryRules.CodePrefix}.named_units_fixed",
                    "Un ítem con unidades nombradas no se ajusta por cantidad: habría que decir cuáles se agregan o quitan.");
            }

            var motivo = InventoryRules.CheckAdjust(item, nuevoTotal, Now);
            if (motivo is not null) return Result.Rejected<StockItem>(motivo);

            var actualizado = item with { OnHand = nuevoTotal };
            _stock.Put(actualizado);
            return Result.Ok(actualizado);
        }
    }

    public Result<StockHold> Hold(string itemId, int quantity, IReadOnlyList<string> unitCodes, Ref forWhat, TimeSpan? ttl, IdempotencyKey key)
    {
        lock (_gate)
        {
            // La llave ANTES que las reglas de estado: un reintento se toparía con su propio
            // apartado y saldría por insufficient_stock. Es la lección de Api.Booking.
            if (_idempotency.Find("hold", key) is { } yaEra)
            {
                var previoItem = _stock.FindByHold(yaEra);
                var previo = previoItem?.Holds.FirstOrDefault(h => h.Id == yaEra);
                return previo is not null
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{InventoryRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el apartado no está.");
            }

            var item = _stock.Find(itemId);
            if (item is null)
            {
                return Rejection.NotFound($"{InventoryRules.CodePrefix}.item_not_found", $"No existe el ítem {itemId}.");
            }

            var vigencia = ttl ?? InventoryRules.DefaultHoldTtl;
            var motivo = InventoryRules.CheckTtl(vigencia);
            if (motivo is not null) return Result.Rejected<StockHold>(motivo);

            var cantidad = item.HasNamedUnits ? unitCodes.Count : quantity;
            motivo = item.HasNamedUnits
                ? InventoryRules.CheckUnits(item, unitCodes, Now)
                : InventoryRules.CheckAvailable(item, cantidad, Now);
            if (motivo is not null) return Result.Rejected<StockHold>(motivo);

            // Con unidades nombradas también hay que comprobar el cupo: el mapa puede tener
            // menos unidades libres que las pedidas aunque ninguna de ESAS esté tomada.
            if (item.HasNamedUnits)
            {
                motivo = InventoryRules.CheckAvailable(item, cantidad, Now);
                if (motivo is not null) return Result.Rejected<StockHold>(motivo);
            }

            var id = Guid.NewGuid().ToString("n");
            var hold = new StockHold(id, cantidad, unitCodes, forWhat, Now + vigencia);
            _stock.Put(item with { Holds = item.Holds.Append(hold).ToList() });
            _idempotency.Remember("hold", key, id);
            return Result.Ok(hold);
        }
    }

    public Result<StockHold> ReleaseHold(string holdId) => Mutate(holdId, (item, hold) =>
    {
        if (hold.ConsumedAtUtc is not null)
        {
            return Rejection.Conflict($"{InventoryRules.CodePrefix}.hold_already_consumed",
                "Ese apartado ya se consumió; devolver existencias es un ajuste, no una liberación.");
        }
        return null;
    }, hold => hold.Released ? hold : hold with { Released = true });

    /// <summary>Consume el apartado: baja las existencias de verdad.</summary>
    public Result<StockHold> ConsumeHold(string holdId) => Mutate(holdId,
        (item, hold) => InventoryRules.CheckHoldUsable(hold, Now),
        hold => hold with { ConsumedAtUtc = Now },
        bajarExistencias: true);

    private Result<StockHold> Mutate(
        string holdId,
        Func<StockItem, StockHold, Rejection?> guarda,
        Func<StockHold, StockHold> cambio,
        bool bajarExistencias = false)
    {
        lock (_gate)
        {
            var item = _stock.FindByHold(holdId);
            var hold = item?.Holds.FirstOrDefault(h => h.Id == holdId);
            if (item is null || hold is null)
            {
                return Rejection.NotFound($"{InventoryRules.CodePrefix}.hold_not_found", $"No existe el apartado {holdId}.");
            }

            var motivo = guarda(item, hold);
            if (motivo is not null) return Result.Rejected<StockHold>(motivo);

            var nuevo = cambio(hold);
            var holds = item.Holds.Select(h => h.Id == holdId ? nuevo : h).ToList();
            _stock.Put(item with
            {
                Holds = holds,
                OnHand = bajarExistencias ? item.OnHand - hold.Quantity : item.OnHand,
            });
            return Result.Ok(nuevo);
        }
    }
}
