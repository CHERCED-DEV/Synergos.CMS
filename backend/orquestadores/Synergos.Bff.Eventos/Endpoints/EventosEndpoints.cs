using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Contracts;
using Synergos.Bff.Eventos.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Eventos.Endpoints;

/// <summary>El ruteo del orquestador de Eventos.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO y su nombre con <c>WithName</c>: de ahí
/// sale el contrato publicado (<c>docs/contracts/openapi/Synergos.Bff.Eventos.json</c>, ADR 0140),
/// el que el UI convierte en tipos. Todavía publica también lo que pone la puerta (comprador,
/// comisión) y las operaciones de operación (reintento, compensaciones): separarlos es de la F3.
/// </remarks>
public static class EventosEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "eventos";

    public static IEndpointRouteBuilder MapEventosEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        // Por eso acepta una llave más corta que la de una capacidad: de ella cuelgan las de cada
        // paso, y una que no cupiera rompería a mitad de la saga (ConLlaveDeSaga).
        app.MapPost("/v1/ticket-purchases", async Task<Results<Created<TicketPurchaseResponse>, ProblemHttpResult>> (
            BuyTicketsRequest req, HttpRequest http, TicketingFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;
            if (string.IsNullOrWhiteSpace(req.EventId)) return Invalid("bad_event", "Hace falta eventId.");

            var buyer = Ref.TryCreate(req.BuyerKind, req.BuyerId);
            if (buyer is null) return Invalid("bad_buyer", "Hacen falta buyerKind y buyerId.");

            if (req.Lines is null || req.Lines.Count == 0) return Invalid("no_lines", "Hace falta al menos una línea.");

            var lineas = req.Lines
                .Select(l => new TicketLine(l.Tier ?? string.Empty, l.Seat, l.Quantity))
                .ToList();

            var r = await flow.BuyAsync(req.EventId!, buyer, lineas, req.ServiceFeePercent ?? 0m, key.Value, ct);

            return r.Map(TicketPurchaseResponse.From).ToCreated(s => $"/v1/ticket-purchases/{s.Id}");
        }).WithName("BuyTickets").ConLlaveDeSaga();

        app.MapGet("/v1/ticket-purchases/{id}", (string id, TicketingFlow flow) =>
            flow.Get(id).Map(TicketPurchaseResponse.From).ToHttp()).WithName("GetTicketPurchase");

        // Confirmar NO recibe cuerpo, al revés que en Tienda: allá hacía falta la dirección de
        // entrega antes de capturar. Una entrada no se despacha, así que no hay nada que validar
        // antes de mover plata.
        app.MapPost("/v1/ticket-purchases/{id}/confirm", async (string id, TicketingFlow flow, CancellationToken ct) =>
            (await flow.ConfirmAsync(id, ct)).Map(TicketPurchaseResponse.From).ToHttp()).WithName("ConfirmTicketPurchase");

        app.MapPost("/v1/ticket-purchases/{id}/cancel", async (string id, TicketingFlow flow, CancellationToken ct) =>
            (await flow.CancelAsync(id, ct)).Map(TicketPurchaseResponse.From).ToHttp()).WithName("CancelTicketPurchase");

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, «se rinde a los ocho intentos» sería «se abandona», y arreglar una devolución
        // colgada exigiría tocarla a mano en la capacidad, por fuera del rastro de la saga.
        app.MapPost("/v1/ticket-purchases/{id}/retry", async (string id, TicketingFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(TicketPurchaseResponse.From).ToHttp()).WithName("RetryTicketPurchase");

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió solo
        // existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", Ok<PageResponse<PendingCompensationResponse>> (int? offset, int? limit, TicketingFlow flow) =>
        {
            var todas = flow.PendingCompensations()
                .SelectMany(s => s.Pending().Select(c => new PendingCompensationResponse(
                    s.Id, c.Kind, c.Reason, c.Attempts, c.NextAttemptUtc, c.LastError,
                    c.IsStuck, s.AlertedAtUtc)))
                .ToList();

            var off = Math.Max(0, offset ?? 0);
            var lim = QueryWindow.Limit(limit);
            return TypedResults.Ok(new PageResponse<PendingCompensationResponse>(
                todas.Skip(off).Take(lim).ToList(), todas.Count, off, off + lim < todas.Count));
        }).WithName("ListCompensations");

        return app;
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{CodePrefix}.{code}", message).ToProblem();
}
