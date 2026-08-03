using Synergos.Core;

namespace Synergos.Api.Inventory.Domain;

/// <summary>Una unidad con nombre y, si aplica, coordenadas.</summary>
/// <param name="Code">Cómo se llama: <c>A-12</c>, <c>fila3-butaca7</c>.</param>
/// <param name="Row">Fila, para pintar un mapa. Opcional.</param>
/// <param name="Column">Columna. Opcional.</param>
/// <remarks>
/// <b>Acá vive la fusión de "Seating" en Inventory</b> (doc 08 §3): un asiento es una unidad de
/// aforo con coordenadas. La geometría es <i>dato</i>, no una capacidad aparte — y esta capacidad
/// no la interpreta: la guarda y la devuelve para que el dominio pinte lo que quiera.
/// </remarks>
public sealed record StockUnit(string Code, int? Row = null, int? Column = null);

/// <summary>Un apartado temporal de existencias.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Quantity">Cuántas unidades aparta.</param>
/// <param name="UnitCodes">Qué unidades concretas, si el ítem las tiene nombradas.</param>
/// <param name="For">Para quién — opaco.</param>
/// <param name="ExpiresAtUtc">Cuándo se suelta solo.</param>
/// <param name="Released">Si se soltó.</param>
/// <param name="ConsumedAtUtc">Cuándo se consumió — deja de ser apartado y pasa a ser salida.</param>
public sealed record StockHold(
    string Id, int Quantity, IReadOnlyList<string> UnitCodes, Ref For,
    DateTimeOffset ExpiresAtUtc, bool Released = false, DateTimeOffset? ConsumedAtUtc = null)
{
    /// <summary>Si sigue reteniendo existencias.</summary>
    public bool IsActive(DateTimeOffset now) => !Released && ConsumedAtUtc is null && now < ExpiresAtUtc;
}

/// <summary>
/// Las existencias de algo.
/// </summary>
/// <param name="Id">Identificador.</param>
/// <param name="Subject">De qué — opaco.</param>
/// <param name="OnHand">Cuántas hay en total.</param>
/// <param name="Units">Unidades nombradas, si las hay. Vacío = solo cantidad.</param>
/// <param name="Holds">Los apartados, activos y pasados.</param>
/// <remarks>
/// <para><b>Por qué es capacidad aparte de Booking.</b> Booking es <i>tiempo</i> —un recurso
/// ocupado de 10:00 a 10:30—; esto es <i>cantidad</i> —quedan tres—. Parecen lo mismo y no lo
/// son: sus índices y sus condiciones de carrera no se parecen en nada, y un modelo que sirva a
/// las dos sirve mal a ambas.</para>
///
/// <para><b>Los apartados NO se borran al consumirse.</b> Un apartado consumido deja de retener
/// existencias pero queda como el rastro de por qué bajó el inventario — sin él, "faltan tres" no
/// tiene explicación.</para>
/// </remarks>
public sealed record StockItem(
    string Id, Ref Subject, int OnHand, IReadOnlyList<StockUnit> Units, IReadOnlyList<StockHold> Holds)
{
    /// <summary>Si las unidades están nombradas una a una.</summary>
    public bool HasNamedUnits => Units.Count > 0;

    /// <summary>Cuántas retienen los apartados vigentes.</summary>
    public int Held(DateTimeOffset now) => Holds.Where(h => h.IsActive(now)).Sum(h => h.Quantity);

    /// <summary>Cuántas se pueden apartar ahora.</summary>
    public int Available(DateTimeOffset now) => OnHand - Held(now);

    /// <summary>Las unidades tomadas por apartados vigentes.</summary>
    public IReadOnlySet<string> TakenUnits(DateTimeOffset now)
        => Holds.Where(h => h.IsActive(now)).SelectMany(h => h.UnitCodes).ToHashSet(StringComparer.OrdinalIgnoreCase);
}
