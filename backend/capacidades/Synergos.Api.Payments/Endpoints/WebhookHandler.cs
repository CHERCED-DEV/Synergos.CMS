using Synergos.Api.Payments.Contracts;
using Synergos.Api.Payments.Domain;
using Synergos.Api.Payments.Transport;
using Synergos.Shared;

namespace Synergos.Api.Payments.Endpoints;

/// <summary>
/// Qué se hace con un evento que manda la pasarela.
/// </summary>
/// <remarks>
/// <para><b>Vive fuera del lambda del endpoint por una razón concreta, no por estilo.</b> Es lo
/// que aprendió <c>Api.Notifications</c> mutando su propio gate: al quitar la verificación de
/// firma del lambda <i>no falló ni un test</i>, porque los del verificador probaban el
/// verificador y ninguno probaba que alguien lo llamara. Un endpoint que mueve plata sin llave
/// compartida no puede depender de que nadie borre esa línea.</para>
///
/// <para><b>Verificar es lo PRIMERO</b>, antes de leer el cuerpo como si significara algo. Al
/// revés, el orden delata: un evento falsificado ya habría sido interpretado para cuando se
/// descubre que no venía de nadie.</para>
/// </remarks>
public static class WebhookHandler
{
    /// <summary>Verifica, traduce y anota. En ese orden, que es el que importa.</summary>
    public static async Task<IResult> HandleAsync(
        WebhookHeaders cabeceras, string cuerpo, WebhookVerifier verificador, PaymentService svc,
        CancellationToken ct = default)
    {
        if (verificador.Verify(cabeceras, cuerpo) is { } malo) return malo.ToProblem();

        var evento = ProviderEventReader.Read(cuerpo);
        if (!evento.IsOk) return evento.Rejection!.ToProblem();

        // Una transacción que sigue en curso se acusa y se suelta: contestar un error haría que
        // el proveedor reintentara durante días algo que decidimos ignorar a propósito.
        if (evento.Value.Status is not { } destino)
        {
            return Results.Accepted(value: new WebhookAck(Matched: false, "el evento no resuelve el cobro"));
        }

        var r = await svc.RecordProviderEventAsync(
            evento.Value.Reference, destino, evento.Value.AmountInCents, ct).ConfigureAwait(false);

        // Una referencia que no es de este despliegue tampoco es un error del proveedor —pasa con
        // transacciones de otra cuenta o de un despliegue anterior—, así que se acusa recibo en
        // vez de dejarlo reintentando. Queda dicho en la respuesta, que es donde se puede ver.
        if (!r.IsOk && r.Rejection!.Code == $"{PaymentRules.CodePrefix}.unknown_provider_reference")
        {
            return Results.Accepted(value: new WebhookAck(Matched: false, r.Rejection.Message));
        }

        return r.Map(PaymentResponse.From).ToHttp();
    }
}
