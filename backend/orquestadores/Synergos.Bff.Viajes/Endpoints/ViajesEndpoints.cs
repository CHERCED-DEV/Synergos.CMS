using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
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
        // Por eso acepta una llave más corta que la de una capacidad: de ella cuelgan las de cada
        // paso, y una que no cupiera rompería a mitad de la saga (ConLlaveDeSaga).
        app.MapPost("/v1/trips", async Task<Results<Created<TripResponse>, ProblemHttpResult>> (
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

            return r.Map(TripResponse.From).ToCreated(s => $"/v1/trips/{s.Id}");
        }).WithName("BookTrip").ConLlaveDeSaga();

        app.MapGet("/v1/trips/{id}", (string id, TripFlow flow) =>
            flow.Get(id).Map(TripResponse.From).ToHttp()).WithName("GetTrip");

        // Confirmar NO recibe cuerpo: un viaje no se despacha, así que no hay dirección que
        // validar antes de mover plata.
        app.MapPost("/v1/trips/{id}/confirm", async (string id, TripFlow flow, CancellationToken ct) =>
            (await flow.ConfirmAsync(id, ct)).Map(TripResponse.From).ToHttp()).WithName("ConfirmTrip");

        // Cancelar admite una penalidad ya calculada por quien vendió. Sin cuerpo se devuelve
        // todo, que es lo correcto cuando no hay política que aplicar.
        app.MapPost("/v1/trips/{id}/cancel", async Task<Results<Ok<TripResponse>, ProblemHttpResult>> (
            string id, CancelTripRequest? req, TripFlow flow, CancellationToken ct) =>
        {
            Money? retener = null;
            if (req?.Retain is { } m && m.Amount > 0m)
            {
                if (!TryMoney(m, out var leida, out var mala)) return mala!;
                retener = leida;
            }
            return (await flow.CancelAsync(id, retener, ct)).Map(TripResponse.From).ToHttp();
        }).WithName("CancelTrip")
          // FUERA DEL CONTRATO, y se conserva igual: el cuerpo es opcional a propósito (sin él se devuelve todo, y
          // HttpTravelCartEngine cancela así), y el traductor del contrato del UI sólo admite cuerpos requeridos
          // (NV-15). Ningún navegador la llama: la llama el CMS. Declarada en RutasFueraDelContratoTests.
          .ExcludeFromDescription();

        // Devolver una PARTE de lo cobrado, para un viaje que salió a medias. Es la otra mitad de
        // la confirmación parcial (#40): acá no se sabe cuánto vale el ítem caído —el viaje se
        // cotiza entero— así que el monto llega calculado, igual que la penalidad de cancelar.
        //
        // EXIGE llave, y no por ceremonia: devolver plata es un movimiento RELATIVO, así que un
        // reintento tras un timeout sin llave devolvería dos veces. Es la misma razón por la que
        // el ajuste relativo de Api.Inventory la exige y el absoluto no (#30).
        app.MapPost("/v1/trips/{id}/refund", async Task<Results<Ok<TripResponse>, ProblemHttpResult>> (
            string id, HttpRequest http, TripFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            var req = await CuerpoOpcional<RefundTripRequest>(http, ct);
            if (req?.Amount is not { } m)
            {
                return Invalid("bad_refund", "Hace falta el monto a devolver.");
            }
            if (!TryMoney(m, out var monto, out var mala)) return mala!;

            return (await flow.RefundAsync(id, monto, req.Reason, key, ct)).Map(TripResponse.From).ToHttp();
        }).WithName("RefundTrip").ConLlaveDeIdempotencia()
          // El cuerpo se publica REQUERIDO porque lo es: sin él no hay monto y contesta 400 viajes.bad_refund. Se lee a
          // mano (y no enlazado) sólo para dar ese código, como siempre, en vez del 400 sin cuerpo del enlazador; enlazado
          // y anulable, el documento lo publicaba opcional.
          .Accepts<RefundTripRequest>("application/json");

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, «se rinde a los ocho intentos» sería «se abandona», y arreglar una
        // devolución colgada exigiría tocarla a mano en la capacidad, fuera del rastro de la saga.
        app.MapPost("/v1/trips/{id}/retry", async (string id, TripFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(TripResponse.From).ToHttp()).WithName("RetryTrip");

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió solo
        // existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", Ok<PageResponse<PendingCompensationResponse>> (int? offset, int? limit, TripFlow flow) =>
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

    /// <summary>
    /// El cuerpo JSON, o nulo si no viene (sin cuerpo, o sin tipo JSON): lo que hacía el enlazador anulable, también con un
    /// JSON mal formado, que sigue siendo un 400 de la petición.
    /// </summary>
    private static async Task<T?> CuerpoOpcional<T>(HttpRequest http, CancellationToken ct) where T : class
    {
        if (!http.HasJsonContentType() || http.ContentLength is 0) return null;
        try
        {
            return await http.ReadFromJsonAsync<T>(ct);
        }
        catch (JsonException e)
        {
            throw new BadHttpRequestException("El cuerpo no es un JSON que se pueda leer.", StatusCodes.Status400BadRequest, e);
        }
    }

    /// <summary>
    /// Lee un monto del cuerpo sin dejar que una moneda mal escrita se convierta en un 500.
    /// </summary>
    /// <remarks>
    /// <c>Money.Of</c> LANZA con un código que no sea ISO 4217, y eso está bien donde lo llama el
    /// dominio —ahí un código malo es un defecto nuestro— pero acá el dato viene de fuera: un
    /// <c>"US"</c> en el JSON es una petición mal formada, y contestarla con «error interno» le
    /// echa la culpa al servidor de algo que hizo quien llamó.
    /// </remarks>
    private static bool TryMoney(MoneyDto dto, out Money money, out ProblemHttpResult? bad)
    {
        var moneda = string.IsNullOrWhiteSpace(dto.Currency) ? Money.Cop : dto.Currency.Trim();
        if (moneda.Length != 3)
        {
            money = default;
            bad = Invalid("bad_currency", $"'{dto.Currency}' no es un código ISO 4217.");
            return false;
        }

        money = Money.Of(dto.Amount, moneda);
        bad = null;
        return true;
    }
}
