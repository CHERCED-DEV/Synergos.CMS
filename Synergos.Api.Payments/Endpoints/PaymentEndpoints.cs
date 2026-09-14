using Synergos.Api.Payments.Contracts;
using Synergos.Api.Payments.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Payments.Endpoints;

/// <summary>El ruteo de cobros.</summary>
public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/payments", async (
            AuthorizeRequest req, HttpRequest http, PaymentService svc, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, PaymentRules.CodePrefix, out var key, out var falta)) return falta!;

            var forWhat = Ref.TryCreate(req.ForKind, req.ForId);
            if (forWhat is null) return Invalid("bad_for", "Hacen falta forKind y forId.");
            var payer = Ref.TryCreate(req.PayerKind, req.PayerId);
            if (payer is null) return Invalid("bad_payer", "Hacen falta payerKind y payerId.");
            if (!TryMoney(req.Amount, out var amount, out var badMoney)) return badMoney!;

            return (await svc.AuthorizeAsync(forWhat, payer, amount, key, ct)).Match(
                p => Results.Created($"/v1/payments/{p.Id}", PaymentResponse.From(p)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/payments/{id}", (string id, PaymentService svc) =>
            svc.Get(id).Map(PaymentResponse.From).ToHttp());

        app.MapGet("/v1/payments", (string? forKind, string? forId, int? offset, int? limit, PaymentService svc) =>
            svc.ListFor(Ref.TryCreate(forKind, forId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<PaymentResponse>(
                    p.Items.Select(PaymentResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/payments/{id}/capture", async (
            string id, HttpRequest http, PaymentService svc, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, PaymentRules.CodePrefix, out var key, out var falta)) return falta!;
            return (await svc.CaptureAsync(id, key, ct)).Map(PaymentResponse.From).ToHttp();
        });

        // Liberar NO lleva llave: la operación ya es idempotente por diseño —liberar lo
        // liberado devuelve lo mismo— y exigir una cabecera que no protege de nada solo
        // enseñaría a los clientes a inventar llaves.
        app.MapPost("/v1/payments/{id}/void", async (string id, PaymentService svc, CancellationToken ct) =>
            (await svc.VoidAsync(id, ct)).Map(PaymentResponse.From).ToHttp());

        app.MapPost("/v1/payments/{id}/refund", async (
            string id, RefundRequest req, HttpRequest http, PaymentService svc, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, PaymentRules.CodePrefix, out var key, out var falta)) return falta!;
            if (!TryMoney(req.Amount, out var amount, out var badMoney)) return badMoney!;

            return (await svc.RefundPaymentAsync(id, amount, req.Reason, key, ct)).Map(PaymentResponse.From).ToHttp();
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
            bad = Invalid("bad_currency", $"'{dto.Currency}' no es un código ISO 4217.");
            return false;
        }
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{PaymentRules.CodePrefix}.{code}", message).ToProblem();
}
