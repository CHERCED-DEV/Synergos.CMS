using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Alquiler.Endpoints;

/// <summary>El borde del orquestador de Alquiler.</summary>
public static class AlquilerEndpoints
{
    /// <summary>El prefijo de los códigos de rechazo de este borde.</summary>
    public const string CodePrefix = "alquiler";

    /// <summary>Cablea las rutas.</summary>
    /// <param name="app">El router.</param>
    /// <returns>El mismo router.</returns>
    public static IEndpointRouteBuilder MapAlquilerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/rentals", async (
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

            return r.Match(
                s => Results.Created($"/v1/rentals/{s.Id}", RentalResponse.From(s)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/rentals/{id}", (string id, RentalFlow flow) =>
            flow.Get(id).Map(RentalResponse.From).ToHttp());

        app.MapPost("/v1/rentals/{id}/return", async (
            string id, SettleRentalRequest? req, RentalFlow flow, CancellationToken ct) =>
        {
            if (!TryMonto(req, out var monto, out var malo))
            {
                return malo!;
            }

            return (await flow.ReturnAsync(id, monto, ct)).Map(RentalResponse.From).ToHttp();
        });

        app.MapPost("/v1/rentals/{id}/cancel", async (
            string id, SettleRentalRequest? req, RentalFlow flow, CancellationToken ct) =>
        {
            if (!TryMonto(req, out var monto, out var malo))
            {
                return malo!;
            }

            return (await flow.CancelAsync(id, monto, ct)).Map(RentalResponse.From).ToHttp();
        });

        return app;
    }

    /// <summary>Cero es legítimo —devolver sin daño— así que la ausencia no se rechaza.</summary>
    private static bool TryMonto(SettleRentalRequest? req, out Money monto, out IResult? malo)
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
    private static bool TryMoney(MoneyPayload? m, out Money monto, out IResult? malo)
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

    private static IResult Invalid(string code, string detail)
        => Rejection.Invalid($"{CodePrefix}.{code}", detail).ToProblem();
}
