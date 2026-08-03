using Synergos.Api.Engagement.Contracts;
using Synergos.Api.Engagement.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Engagement.Endpoints;

/// <summary>El ruteo de las opiniones.</summary>
public static class EngagementEndpoints
{
    public static IEndpointRouteBuilder MapEngagementEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/engagements", (AddEngagementRequest req, HttpRequest http, EngagementService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, EngagementRules.CodePrefix, out var key, out var falta)) return falta!;

            var actor = Ref.TryCreate(req.ActorKind, req.ActorId);
            if (actor is null) return Invalid("bad_actor", "Hacen falta actorKind y actorId.");
            var target = Ref.TryCreate(req.TargetKind, req.TargetId);
            if (target is null) return Invalid("bad_target", "Hacen falta targetKind y targetId.");
            if (!Enum.TryParse<EngagementKind>(req.Kind, ignoreCase: true, out var kind))
            {
                return Invalid("bad_kind", $"'{req.Kind}' no es una clase. Son: {string.Join(", ", Enum.GetNames<EngagementKind>())}.");
            }

            return svc.Add(actor, target, kind, req.Body, req.Rating, req.ReactionCode, req.ParentId, key).Match(
                e => Results.Created($"/v1/engagements/{e.Id}", EngagementResponse.From(e)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/engagements/{id}", (string id, EngagementService svc) =>
            svc.Get(id).Map(EngagementResponse.From).ToHttp());

        app.MapGet("/v1/engagements", (string? targetKind, string? targetId, string? kind, int? offset, int? limit, EngagementService svc) =>
        {
            EngagementKind? filtro = null;
            if (!string.IsNullOrWhiteSpace(kind))
            {
                if (!Enum.TryParse<EngagementKind>(kind, ignoreCase: true, out var k)) return Invalid("bad_kind", $"'{kind}' no es una clase.");
                filtro = k;
            }

            return svc.ListForTarget(Ref.TryCreate(targetKind, targetId), filtro, Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<EngagementResponse>(
                    p.Items.Select(EngagementResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        app.MapGet("/v1/engagements/summary", (string? targetKind, string? targetId, EngagementService svc) =>
            svc.Summarize(Ref.TryCreate(targetKind, targetId)).Map(SummaryResponse.From).ToHttp());

        app.MapPost("/v1/engagements/{id}/visibility", (string id, VisibilityRequest req, EngagementService svc) =>
            svc.SetVisibility(id, req.Visible ?? true).Map(EngagementResponse.From).ToHttp());

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{EngagementRules.CodePrefix}.{code}", message).ToProblem();
}
