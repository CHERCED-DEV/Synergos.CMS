using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Salud.Clients;
using Synergos.Bff.Salud.Contracts;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Salud.Endpoints;

/// <summary>
/// La consulta (003-salud-cita, I3): la agenda del día de un profesional desde Booking (P9), la llegada de cada cita como
/// una instancia de Workflow (P10) y el sello de la nota con la llave del consultorio (P6).
/// </summary>
/// <remarks>
/// <para>Las llama sólo el CMS, con la llave compartida, después de su guardia: quién es del personal lo decide él. Nada
/// clínico pasa por aquí: el paciente es el sujeto con que agendó y la nota llega como su huella.</para>
/// <para><b>La llegada</b> es una instancia del proceso <c>Salud:Llegada:Proceso</c> (<c>salud.llegada.v1</c>, que
/// aprovisiona <c>tools/provisionar.procesos.json</c>) por cita, con el sujeto <c>salud.cita:{reserva}</c>. Arranca al
/// marcar el primer paso, con una llave por cita: marcar dos veces no da dos instancias.</para>
/// </remarks>
public static class ConsultaEndpoints
{
    public const string SujetoDeLaCita = "salud.cita";
    public const string SinLlegada = "agendada";
    public const string RolDelConsultorio = "consultorio";
    public static readonly IReadOnlyList<string> Pasos = ["llegar", "pasar", "atender", "no-vino"];

    /// <summary>Cuántos días cabe pedir de una vez: la agenda es de un día, a lo sumo de una semana.</summary>
    private static readonly TimeSpan VentanaMaxima = TimeSpan.FromDays(7);

    private const int PorPagina = 100;
    private const int PaginasMaximas = 20;

    public static IEndpointRouteBuilder MapConsultaEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/agenda", async Task<Results<Ok<AgendaDelDiaResponse>, ProblemHttpResult>> (
            string? profesional, DateTimeOffset? desde, DateTimeOffset? hasta, SaludCapabilities caps, IConfiguration config,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(profesional))
            {
                return Invalid("bad_professional", "Hace falta el profesional.");
            }
            if (desde is not { } inicio || hasta is not { } fin || fin <= inicio || fin - inicio > VentanaMaxima)
            {
                return Invalid("bad_window", "La ventana va de desde a hasta, de una semana a lo sumo.");
            }

            var clase = config["Salud:Oferta:Clase"] ?? "salud.profesional";
            var recurso = await caps.FindResourceBySubjectAsync(new Ref(clase, profesional.Trim()), ct);
            if (!recurso.IsOk)
            {
                return recurso.Rejection!.ToProblem();
            }

            var citas = new List<CitaDeLaAgenda>();
            for (var pagina = 0; pagina < PaginasMaximas; pagina++)
            {
                var reservas = await caps.ReservasDelRecursoAsync(recurso.Value.Id, pagina * PorPagina, PorPagina, ct);
                if (!reservas.IsOk)
                {
                    return reservas.Rejection!.ToProblem();
                }
                foreach (var r in reservas.Value.Items.Where(r => r.Start < fin && r.End > inicio && r.Status != "Cancelled"))
                {
                    var llegada = await LlegadaDeAsync(caps, r.Id, ct);
                    if (!llegada.IsOk)
                    {
                        return llegada.Rejection!.ToProblem();
                    }
                    citas.Add(new CitaDeLaAgenda(r.Id, r.Start, r.End, r.ForKind, r.ForId, llegada.Value));
                }
                if (!reservas.Value.HasMore)
                {
                    break;
                }
            }
            return TypedResults.Ok(new AgendaDelDiaResponse(citas.OrderBy(c => c.Inicio).ToList()));
        }).WithName("AgendaDelDia");

        app.MapPost("/v1/agenda/{reservaId}/llegada", async Task<Results<Ok<LlegadaResponse>, ProblemHttpResult>> (
            string reservaId, PasoDeLlegadaRequest req, SaludCapabilities caps, IConfiguration config, CancellationToken ct) =>
        {
            if (req.Paso is not { } paso || !Pasos.Contains(paso))
            {
                return Invalid("bad_step", "El paso es llegar, pasar, atender o no-vino.");
            }
            var actor = Ref.TryCreate(req.ActorKind, req.ActorId);
            if (actor is null)
            {
                return Invalid("bad_actor", "Hace falta quién marca el paso.");
            }

            var proceso = config["Salud:Llegada:Proceso"] ?? "salud.llegada.v1";
            var sujeto = new Ref(SujetoDeLaCita, reservaId);
            var instancia = await caps.ArrancarAsync(proceso, sujeto, Llave($"llegada-{reservaId}"), ct);
            if (!instancia.IsOk)
            {
                return instancia.Rejection!.ToProblem();
            }

            var disparada = await caps.DispararAsync(
                instancia.Value.Id, paso, actor, [RolDelConsultorio], Llave($"{reservaId}-{paso}"), ct);
            return disparada.IsOk
                ? TypedResults.Ok(new LlegadaResponse(reservaId, disparada.Value.State))
                : disparada.Rejection!.ToProblem();
        }).WithName("MarcarLlegada");

        app.MapPost("/v1/sellos", async Task<Results<Ok<SelloResponse>, ProblemHttpResult>> (
            SellarRequest req, SaludCapabilities caps, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Llave) || string.IsNullOrWhiteSpace(req.Contenido))
            {
                return Invalid("bad_seal", "Hacen falta la llave y el contenido.");
            }
            var sello = await caps.SellarAsync(req.Llave, req.Contenido, ct);
            return sello.IsOk ? TypedResults.Ok(new SelloResponse(sello.Value.Seal, sello.Value.KeyId)) : sello.Rejection!.ToProblem();
        }).WithName("SellarNota");

        app.MapPost("/v1/sellos/comprobar", async Task<Results<Ok<SelloComprobadoResponse>, ProblemHttpResult>> (
            ComprobarSelloRequest req, SaludCapabilities caps, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Llave) || string.IsNullOrWhiteSpace(req.Contenido) || string.IsNullOrWhiteSpace(req.Sello))
            {
                return Invalid("bad_seal", "Hacen falta la llave, el contenido y el sello.");
            }
            var comprobado = await caps.ComprobarSelloAsync(req.Llave, req.Contenido, req.Sello, ct);
            return comprobado.IsOk
                ? TypedResults.Ok(new SelloComprobadoResponse(comprobado.Value.KeyId))
                : comprobado.Rejection!.ToProblem();
        }).WithName("ComprobarSelloDeNota");

        return app;
    }

    /// <summary>En qué va la llegada de una cita: el estado de su instancia, o «agendada» si nadie la marcó.</summary>
    private static async Task<Result<string>> LlegadaDeAsync(SaludCapabilities caps, string reservaId, CancellationToken ct)
    {
        var instancias = await caps.InstanciasDeAsync(new Ref(SujetoDeLaCita, reservaId), ct);
        if (!instancias.IsOk)
        {
            return Result.Rejected<string>(instancias.Rejection!);
        }
        return Result.Ok(instancias.Value.Items.Count > 0 ? instancias.Value.Items[0].State : SinLlegada);
    }

    private static IdempotencyKey Llave(string valor) => IdempotencyKey.Of(valor);

    private static ProblemHttpResult Invalid(string code, string detail)
        => Rejection.Invalid($"{SaludEndpoints.CodePrefix}.{code}", detail).ToProblem();
}
