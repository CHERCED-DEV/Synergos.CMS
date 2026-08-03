using Synergos.Api.Pricing.Contracts;
using Synergos.Api.Pricing.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Pricing.Endpoints;

/// <summary>El ruteo de precios, promociones y cotizaciones.</summary>
public static class PricingEndpoints
{
    public static IEndpointRouteBuilder MapPricingEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/prices", (SetPriceRequest req, HttpRequest http, PricingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, PricingRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");
            if (!TryMoney(req.Amount, out var amount, out var badMoney)) return badMoney!;

            return svc.SetPrice(subject, amount, req.TaxRateBasisPoints ?? 0, key).Match(
                p => Results.Created($"/v1/prices/{p.Id}", PriceResponse.From(p)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/prices/{id}", (string id, PricingService svc) =>
            svc.GetPrice(id).Map(PriceResponse.From).ToHttp());

        app.MapPost("/v1/promotions", (SavePromotionRequest req, HttpRequest http, PricingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, PricingRules.CodePrefix, out var key, out var falta)) return falta!;

            if (!Enum.TryParse<DiscountKind>(req.Kind, ignoreCase: true, out var kind))
            {
                return Invalid("bad_discount_kind", $"'{req.Kind}' no es un tipo. Son: {string.Join(", ", Enum.GetNames<DiscountKind>())}.");
            }
            if (req.ValidFrom is null || req.ValidTo is null || req.ValidTo <= req.ValidFrom)
            {
                return Invalid("bad_validity", "Hacen falta validFrom y validTo, y validTo tiene que ser posterior.");
            }

            return svc.SavePromotion(req.Code, kind, req.Value ?? 0, (req.Currency ?? Money.Cop).ToUpperInvariant(),
                    TimeWindow.Of(req.ValidFrom.Value, req.ValidTo.Value), key).Match(
                p => Results.Created($"/v1/promotions/{p.Id}", PromotionResponse.From(p)),
                bad => bad.ToProblem());
        });

        // Cotizar es un POST aunque no muta: las líneas no caben razonablemente en una query
        // string, y meterlas ahí las dejaría en logs de proxy y en el historial del navegador.
        app.MapPost("/v1/quotes", (QuoteRequest req, PricingService svc) =>
        {
            var items = new List<(Ref, int)>();
            foreach (var l in req.Lines ?? Array.Empty<QuoteLineRequest>())
            {
                var subject = Ref.TryCreate(l.SubjectKind, l.SubjectId);
                if (subject is null) return Invalid("bad_subject", "Cada línea necesita subjectKind y subjectId.");
                items.Add((subject, l.Quantity ?? 0));
            }

            return svc.Quote(items, req.PromotionCode).Map(QuoteResponse.From).ToHttp();
        });

        return app;
    }

    private static bool TryMoney(MoneyDto? dto, out Money money, out IResult? bad)
    {
        money = default;
        bad = null;
        if (dto is null || string.IsNullOrWhiteSpace(dto.Currency))
        {
            bad = Invalid("bad_money", "Hace falta un monto con su moneda: {amount, currency}.");
            return false;
        }
        try
        {
            money = Money.Of(dto.Amount, dto.Currency);
            return true;
        }
        catch (ArgumentException)
        {
            // Money.Of lanza ante un código que no es ISO 4217. Se traduce acá porque dejarla
            // subir sería un 500, y un 500 dice "reintentá" donde hay que corregir.
            bad = Invalid("bad_currency", $"'{dto.Currency}' no es un código ISO 4217.");
            return false;
        }
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{PricingRules.CodePrefix}.{code}", message).ToProblem();
}
