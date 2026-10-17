using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Alquiler.Contracts;
using Synergos.Bff.Alquiler.Domain;
using Synergos.Bff.Core;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Alquiler.Endpoints;

/// <summary>
/// Lo que pasa con un alquiler de la puerta después del cierre (línea 005, T017; research R5): declarar la
/// devolución y confirmarla. Sin marca de la puerta: las pide el CMS, con su llave compartida, hasta que el núcleo
/// declare las operaciones posteriores al cierre (T051, NN-8).
/// </summary>
public static class LiquidacionEndpoints
{
    /// <summary>La clase de la referencia del personal: fija, nunca del cuerpo (NR-1).</summary>
    public const string KindPersonal = "alquiler.personal";

    /// <summary>Cablea las rutas.</summary>
    /// <param name="app">El router.</param>
    /// <returns>El mismo router.</returns>
    public static IEndpointRouteBuilder MapLiquidacionDeAlquiler(this IEndpointRouteBuilder app)
    {
        // Los alquileres de la puerta del dueño ya no van aquí: «mis alquileres» lee la lista genérica del host
        // (GET /v1/flujos/alquiler.renta, «listar», NN-7), y con ella se retiraron el índice y el paso que anotaba.

        // El inquilino: el dueño lo pone la puerta en X-Synergos-Sujeto; un alquiler ajeno no existe.
        app.MapPost("/v1/alquileres/{id}/declarar-devolucion", async Task<Results<Ok<RentalSettlementResponse>, ProblemHttpResult>> (
            string id, HttpRequest http, LiquidacionDeAlquiler liquidacion, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, AlquilerEndpoints.CodePrefix, out _, out var falta)) return falta!;
            if (!CabecerasDeLaPuerta.TryLeerSujeto(http, out var dueno))
            {
                return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(AlquilerEndpoints.CodePrefix, CabecerasDeLaPuerta.Sujeto),
                    "El sujeto no se puede leer: <kind>:<id>.").ToProblem();
            }
            if (dueno is null)
            {
                return Rejection.Invalid(CabecerasDeLaPuerta.CodigoRequerido(AlquilerEndpoints.CodePrefix, CabecerasDeLaPuerta.Sujeto),
                    "Hace falta el sujeto: lo pone la puerta del CMS.").ToProblem();
            }

            return (await liquidacion.DeclararAsync(id, dueno, ct)).Map(RentalSettlementResponse.From).ToHttp();
        }).WithName("DeclareDoorRentalReturn").ConLlaveDeIdempotencia()
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto, requerida: true);

        // El personal: el CMS ya comprobó el rol; aquí llega su seudónimo y el daño.
        app.MapPost("/v1/alquileres/{id}/confirmar-devolucion", async Task<Results<Ok<RentalSettlementResponse>, ProblemHttpResult>> (
            string id, ConfirmRentalReturnRequest req, HttpRequest http, LiquidacionDeAlquiler liquidacion, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, AlquilerEndpoints.CodePrefix, out _, out var falta)) return falta!;
            if (req.Damage?.Amount is not { } monto)
            {
                return Rejection.Invalid("alquiler.amount_required", "Hace falta el daño, y no se supone cero.").ToProblem();
            }
            if (string.IsNullOrWhiteSpace(req.StaffId))
            {
                return Rejection.Invalid("alquiler.staff_required", "Hace falta quién confirma la devolución.").ToProblem();
            }

            var danio = Money.Of(monto, string.IsNullOrWhiteSpace(req.Damage.Currency) ? Money.Cop : req.Damage.Currency!);
            return (await liquidacion.ConfirmarAsync(id, danio, Ref.Create(KindPersonal, req.StaffId!), ct))
                .Map(RentalSettlementResponse.From).ToHttp();
        }).WithName("ConfirmDoorRentalReturn").ConLlaveDeIdempotencia();

        // Cancelar lo confirmado (T020, research R7): el inquilino con su sujeto de la puerta y sin penalidad; el
        // personal con su seudónimo y la penalidad que fije.
        app.MapPost("/v1/alquileres/{id}/cancelar-confirmado", async Task<Results<Ok<RentalSettlementResponse>, ProblemHttpResult>> (
            string id, CancelDoorRentalRequest req, HttpRequest http, LiquidacionDeAlquiler liquidacion, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, AlquilerEndpoints.CodePrefix, out _, out var falta)) return falta!;
            if (!AlquilerEndpoints.TryActor(req.Actor, out var quien))
            {
                return Rejection.Invalid("alquiler.bad_actor", "Quien cancela es «renter» o «staff».").ToProblem();
            }
            if (!CabecerasDeLaPuerta.TryLeerSujeto(http, out var dueno))
            {
                return Rejection.Invalid(CabecerasDeLaPuerta.CodigoInvalido(AlquilerEndpoints.CodePrefix, CabecerasDeLaPuerta.Sujeto),
                    "El sujeto no se puede leer: <kind>:<id>.").ToProblem();
            }

            Ref? personal = null;
            if (quien == QuienCancela.Personal)
            {
                if (string.IsNullOrWhiteSpace(req.StaffId))
                {
                    return Rejection.Invalid("alquiler.staff_required", "Hace falta quién cancela.").ToProblem();
                }
                personal = Ref.Create(KindPersonal, req.StaffId!);
            }

            var penalidad = req.Penalty?.Amount is { } monto
                ? Money.Of(monto, string.IsNullOrWhiteSpace(req.Penalty.Currency) ? Money.Cop : req.Penalty.Currency!)
                : Money.Zero(Money.Cop);
            return (await liquidacion.CancelarAsync(id, quien, penalidad, dueno, personal, ct))
                .Map(RentalSettlementResponse.From).ToHttp();
        }).WithName("CancelDoorRental").ConLlaveDeIdempotencia()
          .ConCabeceraDeLaPuerta(CabecerasDeLaPuerta.Sujeto);

        return app;
    }
}
