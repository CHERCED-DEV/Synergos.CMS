using Synergos.Api.Moderation.Contracts;
using Synergos.Api.Moderation.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Moderation.Endpoints;

/// <summary>El ruteo de la moderación.</summary>
public static class ModerationEndpoints
{
    public static IEndpointRouteBuilder MapModerationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/cases", (ReportRequest req, HttpRequest http, ModerationService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, ModerationRules.CodePrefix, out var key, out var falta)) return falta!;

            var target = Ref.TryCreate(req.TargetKind, req.TargetId);
            if (target is null) return Invalid("bad_target", "Hacen falta targetKind y targetId.");

            return svc.Report(target, Ref.TryCreate(req.ReporterKind, req.ReporterId), req.Reason, key).Match(
                c => Results.Created($"/v1/cases/{c.Id}", CaseResponse.From(c)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/cases/{id}", (string id, ModerationService svc) =>
            svc.Get(id).Map(CaseResponse.From).ToHttp());

        app.MapGet("/v1/cases", (string? targetKind, string? targetId, int? offset, int? limit, ModerationService svc) =>
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
                return Results.Ok(new PageResponse<CaseResponse>(
                    cola.Items.Select(CaseResponse.From).ToList(), cola.Total, cola.Offset, cola.HasMore));
            }

            return svc.ListForTarget(target, off, lim)
                .Map(p => new PageResponse<CaseResponse>(
                    p.Items.Select(CaseResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        app.MapPost("/v1/cases/{id}/decide", (string id, DecideRequest req, ModerationService svc) =>
        {
            if (!Enum.TryParse<Decision>(req.Decision, ignoreCase: true, out var decision))
            {
                return Invalid("bad_decision", $"'{req.Decision}' no es una decisión. Son: {string.Join(", ", Enum.GetNames<Decision>())}.");
            }

            return svc.Decide(id, decision, Ref.TryCreate(req.DecidedByKind, req.DecidedById), req.Note)
                .Map(CaseResponse.From).ToHttp();
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{ModerationRules.CodePrefix}.{code}", message).ToProblem();
}
