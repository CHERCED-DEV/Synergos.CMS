using Synergos.Api.Pricing.Domain;

namespace Synergos.Api.Pricing.Contracts;

// Lo que cruza el cable, separado de Domain/ (doc 08 §4.1).
//
// El dinero viaja como {amount, currency} y NUNCA como un decimal suelto: un número
// sin moneda es exactamente el error que Money existe para impedir, y dejarlo entrar
// por el borde lo reintroduce por la puerta de atrás.

/// <summary>Un monto tal como llega o sale.</summary>
public sealed record MoneyDto(decimal Amount, string Currency);

/// <summary>Publicar el precio de algo.</summary>
public sealed record SetPriceRequest(string? SubjectKind, string? SubjectId, MoneyDto? Amount, int? TaxRateBasisPoints);

/// <summary>Guardar una promoción.</summary>
public sealed record SavePromotionRequest(
    string? Code, string? Kind, decimal? Value, string? Currency,
    DateTimeOffset? ValidFrom, DateTimeOffset? ValidTo);

/// <summary>Una línea a cotizar.</summary>
public sealed record QuoteLineRequest(string? SubjectKind, string? SubjectId, int? Quantity);

/// <summary>Cotizar unas líneas.</summary>
public sealed record QuoteRequest(IReadOnlyList<QuoteLineRequest>? Lines, string? PromotionCode);

/// <summary>Cómo sale un precio.</summary>
public sealed record PriceResponse(string Id, string SubjectKind, string SubjectId, MoneyDto Amount, int TaxRateBasisPoints, DateTimeOffset UpdatedAtUtc)
{
    public static PriceResponse From(Price p) => new(
        p.Id, p.Subject.Kind, p.Subject.Id, new MoneyDto(p.Amount.Amount, p.Amount.Currency), p.TaxRateBasisPoints, p.UpdatedAtUtc);
}

/// <summary>Cómo sale una promoción.</summary>
public sealed record PromotionResponse(string Id, string Code, string Kind, decimal Value, string Currency, DateTimeOffset ValidFrom, DateTimeOffset ValidTo)
{
    public static PromotionResponse From(Promotion p) => new(
        p.Id, p.Code, p.Kind.ToString(), p.Value, p.Currency, p.Validity.Start, p.Validity.End);
}

/// <summary>Cómo sale una cotización.</summary>
public sealed record QuoteResponse(
    IReadOnlyList<QuoteLineResponse> Lines, MoneyDto Subtotal, MoneyDto Discount, MoneyDto Tax, MoneyDto Total)
{
    public static QuoteResponse From(Quote q) => new(
        q.Lines.Select(QuoteLineResponse.From).ToList(),
        new MoneyDto(q.Subtotal.Amount, q.Subtotal.Currency),
        new MoneyDto(q.Discount.Amount, q.Discount.Currency),
        new MoneyDto(q.Tax.Amount, q.Tax.Currency),
        new MoneyDto(q.Total.Amount, q.Total.Currency));
}

/// <summary>Una línea cotizada.</summary>
public sealed record QuoteLineResponse(string SubjectKind, string SubjectId, int Quantity, MoneyDto UnitPrice, MoneyDto Subtotal, MoneyDto Tax)
{
    public static QuoteLineResponse From(QuoteLine l) => new(
        l.Subject.Kind, l.Subject.Id, l.Quantity,
        new MoneyDto(l.UnitPrice.Amount, l.UnitPrice.Currency),
        new MoneyDto(l.Subtotal.Amount, l.Subtotal.Currency),
        new MoneyDto(l.Tax.Amount, l.Tax.Currency));
}
