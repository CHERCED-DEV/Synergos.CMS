using Synergos.Api.Notifications.Contracts;
using Synergos.Api.Notifications.Domain;
using Synergos.Api.Notifications.Transport;
using Synergos.Shared;

namespace Synergos.Api.Notifications.Endpoints;

/// <summary>
/// Qué se hace con un evento que manda el proveedor.
/// </summary>
/// <remarks>
/// <para><b>Vive fuera del lambda del endpoint por una razón concreta, no por estilo.</b> Al
/// mutar el gate se quitó la verificación de firma del lambda y <i>no falló ni un test</i>: los
/// de <see cref="WebhookVerifier"/> probaban el verificador, y ninguno probaba que alguien lo
/// llamara. Un endpoint que escribe en los datos sin llave compartida no puede depender de que
/// nadie borre esa línea.</para>
/// </remarks>
public static class WebhookHandler
{
    /// <summary>Verifica, traduce y anota. En ese orden, que es el que importa.</summary>
    public static IResult Handle(
        WebhookHeaders cabeceras, string cuerpo, WebhookVerifier verificador, NotificationService svc)
    {
        // Lo primero, siempre: antes de leer el cuerpo como si significara algo.
        if (verificador.Verify(cabeceras, cuerpo) is { } malo) return malo.ToProblem();

        var evento = ProviderEventReader.Read(cuerpo);
        if (!evento.IsOk) return evento.Rejection!.ToProblem();

        // Un evento que no es de estado se acusa y se suelta: si contestáramos un error, el
        // proveedor reintentaría durante días algo que decidimos ignorar a propósito.
        if (evento.Value.Status is not { } estado)
        {
            return Results.Accepted(value: new WebhookAck(Matched: false, "el evento no cambia el estado de un envío"));
        }

        var r = svc.RecordProviderEvent(cabeceras.Id, evento.Value.MessageId, estado);

        // Un mensaje que no es de este despliegue tampoco es un error del proveedor —pasa con
        // correos de otra cuenta o de un despliegue anterior—, así que se acusa recibo en vez de
        // dejarlo reintentando. Queda dicho en la respuesta, que es donde se puede ver.
        if (!r.IsOk && r.Rejection!.Code == $"{NotificationRules.CodePrefix}.unknown_provider_message")
        {
            return Results.Accepted(value: new WebhookAck(Matched: false, r.Rejection.Message));
        }

        return r.Map(DeliveryResponse.From).ToHttp();
    }
}
