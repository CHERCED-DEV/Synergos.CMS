using Synergos.Api.Inventory.Domain;

namespace Synergos.Api.Inventory.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).
//
// En lo que llega en el CUERPO de una petición, todo anulable lleva `= null` (ADR 0140, F2). No
// cambia cómo se liga —si falta el campo, System.Text.Json ya ponía null—, cambia lo que se
// publica: ASP.NET marca `required` todo parámetro posicional sin valor por defecto aunque sea
// anulable, y el contrato exigía campos que el dominio no exige. El esquema publica la forma del
// cable; lo que el negocio exige lo publican los rechazos con su código. Lo vigila el suelo del
// contrato (SueloDelContratoTests).

/// <summary>Una unidad nombrada, con coordenadas opcionales.</summary>
public sealed record StockUnitDto(string? Code = null, int? Row = null, int? Column = null);

/// <summary>Declarar existencias.</summary>
public sealed record DeclareStockRequest(
    string? SubjectKind = null, string? SubjectId = null, int? OnHand = null, IReadOnlyList<StockUnitDto>? Units = null);

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
public sealed record AdjustStockRequest(int? OnHand = null, int? Delta = null);

/// <summary>Apartar existencias.</summary>
public sealed record HoldStockRequest(
    int? Quantity = null, IReadOnlyList<string>? UnitCodes = null, string? ForKind = null, string? ForId = null,
    int? TtlMinutes = null);

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
