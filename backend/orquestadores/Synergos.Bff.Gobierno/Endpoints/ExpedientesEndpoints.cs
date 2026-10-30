using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Gobierno.Contracts;
using Synergos.Bff.Pasos;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Gobierno.Endpoints;

/// <summary>
/// La lectura del expediente de una radicación (009-gobierno-tramite T040): su estado y su historia en la capacidad de
/// procesos. La pide el artefacto del CMS para la bandeja, la cola y el expediente. Sin marca de la puerta: no es del
/// navegador.
/// </summary>
/// <remarks>
/// <para><b>Interino</b> de la lectura de la puerta por acceso (NG-11) y de listar por estado (NG-8): no cuenta para la
/// medida del flujo y se retira con ellas.</para>
///
/// <para><b>La guarda es el CMS</b>, como en lo demás que no es de la puerta: esta ruta la pide sólo él, con la llave, y él
/// comprueba antes que el expediente es de quien lo mira o que quien lo mira es del personal. Nadie se nombra aquí por su
/// palabra: no lee el sujeto de la puerta (<c>PropagacionDeIdentidadTests</c>).</para>
/// </remarks>
public static class ExpedientesEndpoints
{
    /// <summary>El flujo cuyas radicaciones tienen expediente.</summary>
    public const string Radicacion = "gobierno.radicacion";

    public static IEndpointRouteBuilder MapExpedientes(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/expedientes/{radicacion}", async Task<Results<Ok<ExpedienteDeGobiernoResponse>, ProblemHttpResult>> (
            string radicacion, SagaEngine<SagaDeFlujo> sagas, IWorkflowPort procesos, CancellationToken ct) =>
        {
            var saga = sagas.Find(radicacion);
            if (saga is null || saga.Flujo != Radicacion)
            {
                return Rejection.NotFound($"{OfertasEndpoints.CodePrefix}.expediente_no_encontrado", "No hay una radicación con ese id.").ToProblem();
            }
            if (saga.Status != SagaStatus.Completed
                || saga.Datos.GetValueOrDefault("expediente")?.Valor?.GetString() is not { Length: > 0 } expediente)
            {
                return Rejection.Conflict($"{OfertasEndpoints.CodePrefix}.expediente_sin_proceso",
                    $"La radicación está {saga.Status}: su expediente nace al completarse.").ToProblem();
            }

            return (await procesos.LeerAsync(expediente, ct)).Map(i => new ExpedienteDeGobiernoResponse(
                radicacion, i.Id, i.Proceso, i.Estado,
                i.Historia.Select(h => new PasoDelExpedienteResponse(h.Transicion, h.Desde, h.Hacia, h.Por.Kind, h.Por.Id, h.Nota, h.En)).ToList()))
                .ToHttp();
        }).WithName("ReadTramiteCase");

        return app;
    }
}
