using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Api.Moderation.Contracts;
using Synergos.Api.Moderation.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Moderation.Endpoints;

/// <summary>El ruteo de la moderación.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO, su nombre con <c>WithName</c> y lo que lee de la petición
/// —la llave— con su metadato (el molde F2 de la ADR 0140, como <c>Api.Consent</c>): de ahí sale el contrato publicado
/// (<c>docs/contracts/openapi/Synergos.Api.Moderation.json</c>, 012-blogs-social, I5, P6 A), contra el que se cruza el
/// cliente del CMS. Las rutas, los estados y los cuerpos son los de antes.
/// </remarks>
public static class ModerationEndpoints
{
    public static IEndpointRouteBuilder MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cases", Results<Created<CaseResponse>, ProblemHttpResult> (
            ReportRequest req, HttpRequest http, ModerationService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, ModerationRules.CodePrefix, out var key, out var falta)) return falta!;

            var target = Ref.TryCreate(req.TargetKind, req.TargetId);
            if (target is null) return Invalid("bad_target", "Hacen falta targetKind y targetId.");

            return svc.Report(target, Ref.TryCreate(req.ReporterKind, req.ReporterId), req.Reason, key)
                .Map(CaseResponse.From).ToCreated(c => $"/v1/cases/{c.Id}");
        }).WithName("ReportCase").ConLlaveDeIdempotencia();

        app.MapGet("/v1/cases/{id}", (string id, ModerationService svc) =>
            svc.Get(id).Map(CaseResponse.From).ToHttp()).WithName("GetCase");

        app.MapGet("/v1/cases", Results<Ok<PageResponse<CaseResponse>>, ProblemHttpResult> (
            string? targetKind, string? targetId, int? offset, int? limit, ModerationService svc) =>
        {
            var off = Math.Max(0, offset ?? 0);
            var lim = QueryWindow.Limit(limit);
            var target = Ref.TryCreate(targetKind, targetId);

            // Sin objetivo se devuelve LA COLA, que es la vista que de verdad se usa. No es un
            // volcado: solo lo abierto, y acotado — a diferencia de las demás capacidades, acá
            // "todo lo pendiente" es exactamente lo que un moderador necesita ver.
            if (target is null)
            {
                var cola = svc.Queue(off, lim);
                return TypedResults.Ok(new PageResponse<CaseResponse>(
                    cola.Items.Select(CaseResponse.From).ToList(), cola.Total, cola.Offset, cola.HasMore));
            }

            return svc.ListForTarget(target, off, lim)
                .Map(p => new PageResponse<CaseResponse>(
                    p.Items.Select(CaseResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        }).WithName("ListCases");

        app.MapPost("/v1/cases/{id}/decide", Results<Ok<CaseResponse>, ProblemHttpResult> (
            string id, DecideRequest req, ModerationService svc) =>
        {
            if (!Enum.TryParse<Decision>(req.Decision, ignoreCase: true, out var decision))
            {
                return Invalid("bad_decision", $"'{req.Decision}' no es una decisión. Son: {string.Join(", ", Enum.GetNames<Decision>())}.");
            }

            return svc.Decide(id, decision, Ref.TryCreate(req.DecidedByKind, req.DecidedById), req.Note)
                .Map(CaseResponse.From).ToHttp();
        }).WithName("DecideCase");

        return app;
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{ModerationRules.CodePrefix}.{code}", message).ToProblem();
}
