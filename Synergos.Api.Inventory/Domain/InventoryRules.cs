using Synergos.Core;

namespace Synergos.Api.Inventory.Domain;

/// <summary>Lo que el inventario rechaza <b>solo</b>.</summary>
public static class InventoryRules
{
    public const string CodePrefix = "inventory";

    public static readonly TimeSpan DefaultHoldTtl = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MaxHoldTtl = TimeSpan.FromHours(4);

    /// <summary>Si hay existencias para apartar esa cantidad.</summary>
    public static Rejection? CheckAvailable(StockItem item, int quantity, DateTimeOffset now)
    {
        if (quantity <= 0)
        {
            return Rejection.Invalid($"{CodePrefix}.bad_quantity", "La cantidad tiene que ser mayor que cero.");
        }
        var libres = item.Available(now);
        return quantity <= libres
            ? null
            : Rejection.Conflict($"{CodePrefix}.insufficient_stock",
                $"Se pidieron {quantity} y quedan {libres} de {item.OnHand} (el resto está apartado).");
    }

    /// <summary>Si las unidades concretas se pueden tomar.</summary>
    /// <remarks>
    /// Los dos "no" se distinguen porque llevan a acciones distintas: una unidad <b>inexistente</b>
    /// es un error del llamador —eligió una butaca que no está en el mapa—, y una <b>ya tomada</b>
    /// es una carrera perdida contra otra persona que hay que resolver eligiendo otra.
    /// </remarks>
    public static Rejection? CheckUnits(StockItem item, IReadOnlyList<string> codes, DateTimeOffset now)
    {
        if (codes.Count == 0)
        {
            return Rejection.Invalid($"{CodePrefix}.units_required",
                "Este ítem tiene unidades nombradas: hay que decir cuáles, no solo cuántas.");
        }

        var existentes = item.Units.Select(u => u.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var inexistente = codes.FirstOrDefault(c => !existentes.Contains(c));
        if (inexistente is not null)
        {
            return Rejection.NotFound($"{CodePrefix}.unit_not_found", $"La unidad '{inexistente}' no existe en este ítem.");
        }

        var tomadas = item.TakenUnits(now);
        var ocupada = codes.FirstOrDefault(tomadas.Contains);
        return ocupada is not null
            ? Rejection.Conflict($"{CodePrefix}.unit_taken", $"La unidad '{ocupada}' ya está apartada.")
            : null;
    }

    /// <summary>Si el apartado sigue sirviendo.</summary>
    public static Rejection? CheckHoldUsable(StockHold hold, DateTimeOffset now)
    {
        if (hold.ConsumedAtUtc is not null)
        {
            return Rejection.Conflict($"{CodePrefix}.hold_already_consumed", "Ese apartado ya se consumió.");
        }
        if (hold.Released)
        {
            return Rejection.Conflict($"{CodePrefix}.hold_released", "Ese apartado ya se soltó.");
        }
        return now >= hold.ExpiresAtUtc
            ? Rejection.Expired($"{CodePrefix}.hold_expired", $"El apartado venció en {hold.ExpiresAtUtc:O}.")
            : null;
    }

    /// <summary>Si el ajuste de existencias deja el inventario coherente.</summary>
    /// <remarks>
    /// No se puede bajar el total por debajo de lo ya apartado: eso dejaría apartados que nadie
    /// puede cumplir, y el error saldría al entregar en vez de al ajustar.
    /// </remarks>
    public static Rejection? CheckAdjust(StockItem item, int nuevoTotal, DateTimeOffset now)
    {
        if (nuevoTotal < 0)
        {
            return Rejection.Invalid($"{CodePrefix}.negative_stock", "Las existencias no pueden ser negativas.");
        }
        var apartadas = item.Held(now);
        return nuevoTotal < apartadas
            ? Rejection.Conflict($"{CodePrefix}.below_held",
                $"Hay {apartadas} unidades apartadas y se quiso dejar el total en {nuevoTotal}.")
            : null;
    }

    public static Rejection? CheckTtl(TimeSpan ttl)
        => ttl > TimeSpan.Zero && ttl <= MaxHoldTtl
            ? null
            : Rejection.Invalid($"{CodePrefix}.bad_ttl", $"El TTL del apartado va entre 0 y {MaxHoldTtl}.");
}
