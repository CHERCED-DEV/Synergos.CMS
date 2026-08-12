using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Contracts;
using Synergos.Bff.Viajes.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Viajes.Endpoints;

/// <summary>El ruteo del orquestador de Viajes.</summary>
public static class ViajesEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "viajes";

    public static IEndpointRouteBuilder MapViajesEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        app.MapPost("/v1/trips", async (
            BookTripRequest req, HttpRequest http, TripFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            var traveller = Ref.TryCreate(req.TravellerKind, req.TravellerId);
            if (traveller is null) return Invalid("bad_traveller", "Hacen falta travellerKind y travellerId.");

            if (req.Items is null || req.Items.Count == 0) return Invalid("no_items", "Hace falta al menos un ítem.");

            var items = req.Items
                .Select(i => new TripItem(
                    i.ProductRef ?? string.Empty,
                    string.IsNullOrWhiteSpace(i.ProductLabel) ? (i.ProductRef ?? string.Empty) : i.ProductLabel!,
                    i.Start ?? default,
                    i.End ?? default))
                .ToList();

            // El modo lo elige quien vende y el default es el de siempre: todo-o-nada. Un cliente
            // viejo que no lo mande sigue teniendo exactamente el comportamiento que tenía.
            var r = await flow.BookAsync(traveller, items, key.Value, ct, req.PartialConfirm ?? false);

            return r.Match(
                s => Results.Created($"/v1/trips/{s.Id}", TripResponse.From(s)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/trips/{id}", (string id, TripFlow flow) =>
            flow.Get(id).Map(TripResponse.From).ToHttp());

        // Confirmar NO recibe cuerpo: un viaje no se despacha, así que no hay dirección que
        // validar antes de mover plata.
        app.MapPost("/v1/trips/{id}/confirm", async (string id, TripFlow flow, CancellationToken ct) =>
            (await flow.ConfirmAsync(id, ct)).Map(TripResponse.From).ToHttp());

        // Cancelar admite una penalidad ya calculada por quien vendió. Sin cuerpo se devuelve
        // todo, que es lo correcto cuando no hay política que aplicar.
        app.MapPost("/v1/trips/{id}/cancel", async (
            string id, CancelTripRequest? req, TripFlow flow, CancellationToken ct) =>
        {
            Money? retener = null;
            if (req?.Retain is { } m && m.Amount > 0m)
            {
                retener = Money.Of(m.Amount, string.IsNullOrWhiteSpace(m.Currency) ? Money.Cop : m.Currency);
            }
            return (await flow.CancelAsync(id, retener, ct)).Map(TripResponse.From).ToHttp();
        });

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, «se rinde a los ocho intentos» sería «se abandona», y arreglar una
        // devolución colgada exigiría tocarla a mano en la capacidad, fuera del rastro de la saga.
        app.MapPost("/v1/trips/{id}/retry", async (string id, TripFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(TripResponse.From).ToHttp());

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió solo
        // existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", (int? offset, int? limit, TripFlow flow) =>
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
