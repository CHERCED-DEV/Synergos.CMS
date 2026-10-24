using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Alquiler.Endpoints;

/// <summary>El borde del orquestador de Alquiler.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO y su nombre con <c>WithName</c>: de ahí sale el
/// contrato publicado (<c>docs/contracts/openapi/Synergos.Bff.Alquiler.json</c>, ADR 0140), que el CMS cruza
/// y el UI convierte en tipos. Estas rutas son las del flujo imperativo (<c>RentalFlow</c>), las que usa la
/// ruta directa del CMS con <c>Synergos:Alquiler:Mode=Bff</c>; ninguna lleva marca de la puerta.
/// </remarks>
public static class AlquilerEndpoints
{
    /// <summary>El prefijo de los códigos de rechazo de este borde.</summary>
    public const string CodePrefix = "alquiler";

    /// <summary>Cablea las rutas.</summary>
    /// <param name="app">El router.</param>
    /// <returns>El mismo router.</returns>
    public static IEndpointRouteBuilder MapAlquilerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/rentals", async Task<Results<Created<RentalResponse>, ProblemHttpResult>> (
            ReserveRentalRequest req, HttpRequest http, RentalFlow flow, CancellationToken ct) =>
        {
            // La llave es obligatoria: reservar retiene plata, y un reintento sin llave la
            // retiene dos veces.
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta))
            {
                return falta!;
            }

            var renter = Ref.TryCreate(req.RenterKind, req.RenterId);
            if (renter is null)
            {
                return Invalid("bad_renter", "Hacen falta renterKind y renterId.");
            }

            if (req.Start is null || req.End is null || req.End <= req.Start)
            {
                return Invalid("bad_window", "La devolución tiene que ser posterior al retiro.");
            }

            if (!TryMoney(req.RentalTotal, out var total, out var malTotal))
            {
                return malTotal!;
            }

            if (!TryMoney(req.Deposit, out var garantia, out var malaGarantia))
            {
                return malaGarantia!;
            }

            var r = await flow.ReserveAsync(
                renter, req.EquipmentId ?? string.Empty, req.Quantity ?? 1,
                TimeWindow.Of(req.Start.Value, req.End.Value), total, garantia, key.Value, ct);

            return r.Map(RentalResponse.From).ToCreated(s => $"/v1/rentals/{s.RentalId}");
        }).WithName("ReserveRental").ConLlaveDeSaga();

        app.MapGet("/v1/rentals/{id}", Results<Ok<RentalResponse>, ProblemHttpResult> (string id, RentalFlow flow) =>
            flow.Get(id).Map(RentalResponse.From).ToHttp()).WithName("GetRental");

        // Devolver y cancelar exigen el cuerpo, aunque su monto pueda faltar (cero es legítimo): el CMS
        // siempre lo manda, y un cuerpo opcional no se puede traducir a un tipo en el UI.
        app.MapPost("/v1/rentals/{id}/return", async Task<Results<Ok<RentalResponse>, ProblemHttpResult>> (
            string id, SettleRentalRequest req, RentalFlow flow, CancellationToken ct) =>
        {
            if (!TryMonto(req, out var monto, out var malo))
            {
                return malo!;
            }

            return (await flow.ReturnAsync(id, monto, ct)).Map(RentalResponse.From).ToHttp();
        }).WithName("ReturnRental");

        // Cancelar lo confirmado (research R7): quién cancela decide la regla y la penalidad. Sin actor, el
        // inquilino: es lo más restrictivo, y un cliente viejo que no lo mande no cobra penalidades por error.
        app.MapPost("/v1/rentals/{id}/cancel", async Task<Results<Ok<RentalResponse>, ProblemHttpResult>> (
            string id, CancelRentalRequest req, RentalFlow flow, CancellationToken ct) =>
        {
            if (!TryMonto(new SettleRentalRequest(req.Amount), out var monto, out var malo))
            {
                return malo!;
            }

            if (!TryActor(req.Actor, out var actor))
            {
                return Invalid("bad_actor", "Quien cancela es «renter» o «staff».");
            }

            return (await flow.CancelAsync(id, monto, actor, ct)).Map(RentalResponse.From).ToHttp();
        }).WithName("CancelRental");

        // Declarar la devolución (decisión 5 del lote 1): sin cuerpo y sin plata; repetirla devuelve lo mismo.
        app.MapPost("/v1/rentals/{id}/declare-return", async Task<Results<Ok<RentalResponse>, ProblemHttpResult>> (
            string id, RentalFlow flow, CancellationToken ct) =>
            (await flow.DeclareReturnAsync(id, ct)).Map(RentalResponse.From).ToHttp()).WithName("DeclareRentalReturn");

        // La oferta de un equipo (lote 3, 005-P1): el precio de cada tramo y la garantía en Pricing, y el
        // recurso con sus unidades en Booking. La llama el CMS al publicar la ficha, no un navegador, así que no
        // lleva marca de la puerta. La llave es una por publicación (la pone el CMS): repetirla con ella no duplica
        // nada, y una publicación nueva vuelve a fijar los precios aunque regrese a un valor de antes; ver OfertaDeAlquiler.
        app.MapPost("/v1/ofertas", async Task<Results<Ok<EquipmentOfferResponse>, ProblemHttpResult>> (
            PublishEquipmentOfferRequest req, HttpRequest http, OfertaDeAlquiler ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var publicacion, out var falta)) return falta!;

            var oferta = new OfertaDeEquipo(req.EquipmentId, req.Currency, req.DailyRate,
                req.Rates?.Select(t => new TramoDeTarifa(t.MinDays ?? 0, t.PerDay ?? 0m)).ToList(),
                req.DepositPerUnit, req.MinDays, req.MaxDays, req.Units);
            return (await ofertas.PublicarAsync(oferta, publicacion, ct)).Map(EquipmentOfferResponse.From).ToHttp();
        }).WithName("PublishEquipmentOffer").ConLlaveDeIdempotencia();

        app.MapPost("/v1/ofertas/{equipmentId}/retirar", async Task<Results<Ok<EquipmentOfferResponse>, ProblemHttpResult>> (
            string equipmentId, HttpRequest http, OfertaDeAlquiler ofertas, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out _, out var falta)) return falta!;
            return (await ofertas.RetirarAsync(equipmentId, ct)).Map(EquipmentOfferResponse.From).ToHttp();
        }).WithName("RetireEquipmentOffer").ConLlaveDeIdempotencia();

        return app;
    }

    /// <summary>Quién cancela, en el cable: <c>renter</c> (o nada) y <c>staff</c>.</summary>
    internal static bool TryActor(string? valor, out QuienCancela actor)
    {
        switch (valor?.Trim().ToLowerInvariant())
        {
            case null or "" or "renter":
                actor = QuienCancela.Inquilino;
                return true;
            case "staff":
                actor = QuienCancela.Personal;
                return true;
            default:
                actor = QuienCancela.Inquilino;
                return false;
        }
    }

    /// <summary>Cero es legítimo —devolver sin daño— así que la ausencia no se rechaza.</summary>
    private static bool TryMonto(SettleRentalRequest? req, out Money monto, out ProblemHttpResult? malo)
    {
        if (req?.Amount is null)
        {
            monto = Money.Zero(Money.Cop);
            malo = null;
            return true;
        }

        return TryMoney(req.Amount, out monto, out malo);
    }

    /// <summary>
    /// Un monto AUSENTE se rechaza donde es obligatorio.
    /// </summary>
    /// <remarks>
    /// No se cae a cero: «cero» es un precio válido que no falla en ninguna parte hasta que
    /// alguien mira la factura (<c>bash_ifs_whitespace_shifts_fields</c>, el mismo corte).
    /// </remarks>
    private static bool TryMoney(MoneyPayload? m, out Money monto, out ProblemHttpResult? malo)
    {
        monto = Money.Zero(Money.Cop);
        malo = null;

        if (m?.Amount is null)
        {
            malo = Invalid("amount_required", "Hace falta el monto, y no se supone cero.");
            return false;
        }

        var moneda = string.IsNullOrWhiteSpace(m.Currency) ? Money.Cop : m.Currency!;
        monto = Money.Of(m.Amount.Value, moneda);
        return true;
    }

    private static ProblemHttpResult Invalid(string code, string detail)
        => Rejection.Invalid($"{CodePrefix}.{code}", detail).ToProblem();
}
