using Microsoft.Extensions.Options;
using Synergos.Api.Workflow.Contracts;
using Synergos.Api.Workflow.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Workflow.Endpoints;

/// <summary>El ruteo de los procesos.</summary>
public static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/definitions", (DefineRequest req, HttpRequest http, WorkflowService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, WorkflowRules.CodePrefix, out var key, out var falta)) return falta!;

            var transiciones = new List<TransitionRule>();
            foreach (var t in req.Transitions ?? Array.Empty<TransitionDto>())
            {
                if (string.IsNullOrWhiteSpace(t.Name) || string.IsNullOrWhiteSpace(t.From) || string.IsNullOrWhiteSpace(t.To))
                {
                    return Invalid("bad_transition", "Cada transición necesita name, from y to.");
                }
                transiciones.Add(new TransitionRule(t.Name!.Trim(), t.From!.Trim(), t.To!.Trim(),
                    (t.RequiredRoles ?? Array.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r.Trim()).ToList()));
            }

            var finales = (req.FinalStates ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).ToList();

            return svc.Define(req.Key, req.InitialState, finales, transiciones, key).Match(
                d => Results.Created($"/v1/definitions/{d.Key}", DefinitionResponse.From(d)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/definitions/{key}", (string key, WorkflowService svc) =>
            svc.GetDefinition(key).Map(DefinitionResponse.From).ToHttp());

        app.MapPost("/v1/instances", (StartRequest req, HttpRequest http, WorkflowService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, WorkflowRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.Start(req.DefinitionKey, subject, key).Match(
                i => Results.Created($"/v1/instances/{i.Id}", InstanceResponse.From(i)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/instances/{id}", (string id, WorkflowService svc) =>
            svc.GetInstance(id).Map(InstanceResponse.From).ToHttp());

        app.MapGet("/v1/instances", (string? subjectKind, string? subjectId, int? offset, int? limit, WorkflowService svc) =>
            svc.ListForSubject(Ref.TryCreate(subjectKind, subjectId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<InstanceResponse>(
                    p.Items.Select(InstanceResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/instances/{id}/fire", (
            string id, FireRequest req, HttpRequest http, WorkflowService svc,
            IdentityTokenGate identidad, IOptions<WorkflowRoleOptions> roles, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, WorkflowRules.CodePrefix, out var key, out var falta)) return falta!;

            var principal = Ref.TryCreate(req.ActorKind, req.ActorId);
            if (principal is null) return Invalid("bad_actor", "Hacen falta actorKind y actorId.");

            // LOS ROLES NO SE LE CREEN AL LLAMADOR SI TRAE CON QUÉ PROBARLOS (defecto #48).
            // Antes salían del cuerpo y punto, así que cualquiera con la llave compartida se
            // ascendía a funcionario escribiendo una línea de JSON. La regla vive en el dominio;
            // acá sólo se lee la cabecera y se rutea.
            var quien = WorkflowRules.ResolveActor(
                identidad, http.Headers[IdentityTokens.HeaderName].FirstOrDefault(),
                principal, req.ActorRoles, clock.GetUtcNow());

            if (!quien.IsOk) return quien.Rejection!.ToProblem();

            return svc.Fire(id, req.Transition, quien.Value.Actor, req.Note, key,
                    quien.Value.Verified, roles.Value.RequireVerifiedRoles)
                .Map(InstanceResponse.From).ToHttp();
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{WorkflowRules.CodePrefix}.{code}", message).ToProblem();
}
