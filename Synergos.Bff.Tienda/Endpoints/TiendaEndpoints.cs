using Synergos.Bff.Core;
using Synergos.Bff.Tienda.Contracts;
using Synergos.Bff.Tienda.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Tienda.Endpoints;

/// <summary>El ruteo del orquestador de Tienda.</summary>
public static class TiendaEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "tienda";

    public static IEndpointRouteBuilder MapTiendaEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        app.MapPost("/v1/purchases", async (BuyRequest req, HttpRequest http, PurchaseFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            if (string.IsNullOrWhiteSpace(req.CartId)) return Invalid("bad_cart", "Hace falta cartId.");

            var r = await flow.BuyAsync(req.CartId!, key.Value, ct);

            return r.Match(
                s => Results.Created($"/v1/purchases/{s.Id}", PurchaseResponse.From(s)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/purchases/{id}", (string id, PurchaseFlow flow) =>
            flow.Get(id).Map(PurchaseResponse.From).ToHttp());

        app.MapPost("/v1/purchases/{id}/confirm", async (string id, ConfirmRequest req, PurchaseFlow flow, CancellationToken ct) =>
        {
            if (req.To is null || string.IsNullOrWhiteSpace(req.To.Line1) || string.IsNullOrWhiteSpace(req.To.City))
            {
                // Se comprueba ANTES de capturar. Descubrir que falta la dirección después de
                // mover plata costaría una devolución por un campo de formulario.
                return Invalid("bad_address", "Hace falta una dirección de entrega con línea y ciudad.");
            }
            if (string.IsNullOrWhiteSpace(req.Carrier)) return Invalid("bad_carrier", "Hace falta la transportadora.");

            return (await flow.ConfirmAsync(id, req.To, req.Carrier!, ct)).Map(PurchaseResponse.From).ToHttp();
        });

        app.MapPost("/v1/purchases/{id}/cancel", async (string id, PurchaseFlow flow, CancellationToken ct) =>
            (await flow.CancelAsync(id, ct)).Map(PurchaseResponse.From).ToHttp());

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, "se rinde a los ocho intentos" sería "se abandona", y arreglar una devolución
        // colgada exigiría tocarla a mano en la capacidad, por fuera del rastro de la saga.
        app.MapPost("/v1/purchases/{id}/retry", async (string id, PurchaseFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(PurchaseResponse.From).ToHttp());

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió
        // solo existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", (int? offset, int? limit, PurchaseFlow flow) =>
        {
            var todas = flow.PendingCompensations()
                .SelectMany(s => s.Pending().Select(c => new PendingCompensationResponse(
                    s.Id, c.Kind, c.Reason, c.Attempts, c.NextAttemptUtc, c.LastError,
                    c.IsStuck, s.AlertedAtUtc)))
                .ToList();

            var off = Math.Max(0, offset ?? 0);
            var lim = QueryWindow.Limit(limit);
            return Results.Ok(new PageResponse<PendingCompensationResponse>(
                todas.Skip(off).Take(lim).ToList(), todas.Count, off, off + lim < todas.Count));
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{CodePrefix}.{code}", message).ToProblem();
}
