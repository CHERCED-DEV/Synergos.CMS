using Synergos.Api.Inventory.Domain;

namespace Synergos.Api.Inventory.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).

/// <summary>Una unidad nombrada, con coordenadas opcionales.</summary>
public sealed record StockUnitDto(string? Code, int? Row, int? Column);

/// <summary>Declarar existencias.</summary>
public sealed record DeclareStockRequest(
    string? SubjectKind, string? SubjectId, int? OnHand, IReadOnlyList<StockUnitDto>? Units);

/// <summary>
/// Ajustar existencias, de las dos maneras que existen — y son distintas de verdad.
/// </summary>
/// <remarks>
/// <para><b><c>Delta</c> es relativo</b> («entraron 3», «devolvieron 2») y es el que hay que usar
/// cuando lo que se sabe es <i>cuánto cambió</i>. Es la forma segura frente a otro escritor: la
/// suma la hace la capacidad sobre lo que hay, no el llamador sobre lo que leyó hace un rato.
/// Exige <c>Idempotency-Key</c>, porque un relativo reintentado suma dos veces.</para>
///
/// <para><b><c>OnHand</c> es absoluto</b> («conté y hay 47») y es el que hay que usar cuando lo
/// que se sabe es <i>cuánto hay</i> — un recuento físico. No exige llave porque repetirlo no
/// cambia nada: dejar el total en 47 dos veces deja 47.</para>
///
/// <para><b>Va exactamente uno de los dos.</b> Los dos juntos serían dos órdenes contradictorias
/// en el mismo cuerpo, y ninguno no es una orden.</para>
/// </remarks>
public sealed record AdjustStockRequest(int? OnHand, int? Delta);

/// <summary>Apartar existencias.</summary>
public sealed record HoldStockRequest(
    int? Quantity, IReadOnlyList<string>? UnitCodes, string? ForKind, string? ForId, int? TtlMinutes);

/// <summary>Cómo sale un ítem.</summary>
public sealed record StockItemResponse(
    string Id, string SubjectKind, string SubjectId, int OnHand, int Held, int Available,
    IReadOnlyList<StockUnitDto> Units, IReadOnlyList<string> TakenUnits)
{
    public static StockItemResponse From(StockItem i, DateTimeOffset now) => new(
        i.Id, i.Subject.Kind, i.Subject.Id, i.OnHand, i.Held(now), i.Available(now),
        i.Units.Select(u => new StockUnitDto(u.Code, u.Row, u.Column)).ToList(),
        i.TakenUnits(now).OrderBy(c => c, StringComparer.Ordinal).ToList());
}

/// <summary>Cómo sale un apartado.</summary>
public sealed record StockHoldResponse(
    string Id, int Quantity, IReadOnlyList<string> UnitCodes, string ForKind, string ForId,
    DateTimeOffset ExpiresAtUtc, bool Released, DateTimeOffset? ConsumedAtUtc)
{
    public static StockHoldResponse From(StockHold h) => new(
        h.Id, h.Quantity, h.UnitCodes, h.For.Kind, h.For.Id, h.ExpiresAtUtc, h.Released, h.ConsumedAtUtc);
}
