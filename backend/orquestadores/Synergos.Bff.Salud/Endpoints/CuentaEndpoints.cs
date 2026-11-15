using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Contracts;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Salud.Endpoints;

/// <summary>
/// El estado de cuenta del paciente (003-salud-cita, I7 P17): una línea por cita suya cobrada, con lo que dice Payments de
/// su cobro —lo capturado y lo devuelto—, así que el total es el de Payments.
/// </summary>
/// <remarks>
/// <para><b>Sale de las citas, no de una lista de pagos por pagador</b>, que <c>Api.Payments</c> no tiene (lista por «para
/// qué»): cada cita por la puerta guarda su cobro en su saga, y se lee ese cobro. Una cita sin copago no tiene línea, y un
/// cobro que no llegó a capturarse (anulado, o pendiente de confirmar) tampoco: no se movió plata.</para>
///
/// <para><b>Una cuenta a medias no se da</b>: si un cobro no se puede leer, contesta el rechazo y no la cuenta sin él.</para>
///
/// <para>La llama sólo el CMS, con la llave compartida, con el sujeto del paciente de la sesión.</para>
/// </remarks>
public static class CuentaEndpoints
{
    /// <summary>El flujo de la cita, el que guarda el cobro.</summary>
    public const string FlujoDeLaCita = "salud.cita";

    /// <summary>El estado de un cobro en el que la plata se movió.</summary>
    public const string Capturado = "Captured";

    public static IEndpointRouteBuilder MapCuentaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/estado-de-cuenta", async Task<Results<Ok<EstadoDeCuentaResponse>, ProblemHttpResult>> (
            string? pacienteKind, string? pacienteId, ISagaStore<SagaDeFlujo> sagas, SaludCapabilities caps, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(pacienteKind) || string.IsNullOrWhiteSpace(pacienteId))
            {
                return Rejection.Invalid($"{SaludEndpoints.CodePrefix}.bad_patient", "Hace falta el paciente.").ToProblem();
            }

            var lineas = new List<LineaDeCuenta>();
            foreach (var cita in sagas.DeDueno(new Ref(pacienteKind.Trim(), pacienteId.Trim()))
                         .Where(s => string.Equals(s.Flujo, FlujoDeLaCita, StringComparison.Ordinal)))
            {
                if (Texto(cita, "cobroId") is not { } cobroId)
                {
                    continue;
                }
                var pago = await caps.GetPaymentAsync(cobroId, ct);
                if (!pago.IsOk)
                {
                    return pago.Rejection!.ToProblem();
                }
                if (!string.Equals(pago.Value.Status, Capturado, StringComparison.Ordinal))
                {
                    continue;
                }
                var cobrado = pago.Value.Amount.Amount;
                var devuelto = pago.Value.Refunded?.Amount ?? cobrado - pago.Value.Refundable.Amount;
                lineas.Add(new LineaDeCuenta(
                    cita.Id, SagaDeFlujo.NumeroDe(cita.Id), Inicio(cita), cobrado, devuelto, pago.Value.Amount.Currency));
            }
            return TypedResults.Ok(new EstadoDeCuentaResponse(lineas.OrderByDescending(l => l.Inicio).ToList()));
        }).WithName("EstadoDeCuenta");

        return app;
    }

    private static string? Texto(SagaDeFlujo saga, string nombre)
        => saga.Datos.TryGetValue(nombre, out var dato) && dato.Valor is { ValueKind: JsonValueKind.String } v
            && v.GetString() is { Length: > 0 } texto ? texto : null;

    /// <summary>Cuándo empieza la cita, de su franja; <c>null</c> si no la guarda.</summary>
    private static DateTimeOffset? Inicio(SagaDeFlujo saga)
        => saga.Datos.TryGetValue("ventana", out var dato) && dato.Valor is { ValueKind: JsonValueKind.Object } v
            && v.TryGetProperty("start", out var inicio) && inicio.TryGetDateTimeOffset(out var cuando) ? cuando : null;
}
