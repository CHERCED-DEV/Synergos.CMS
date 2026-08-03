using Synergos.Api.Consent.Contracts;
using Synergos.Api.Consent.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Consent.Endpoints;

/// <summary>El ruteo del consentimiento.</summary>
public static class ConsentEndpoints
{
    public static IEndpointRouteBuilder MapConsentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/grants", (GrantRequest req, HttpRequest http, ConsentService svc, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, ConsentRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.Grant(subject, req.Purpose, req.PolicyVersion, req.ExpiresAtUtc, key).Match(
                g => Results.Created($"/v1/grants/{g.Id}", ConsentResponse.From(g, clock.GetUtcNow())),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/grants/{id}", (string id, ConsentService svc, TimeProvider clock) =>
            svc.Get(id).Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToHttp());

        app.MapGet("/v1/grants", (string? subjectKind, string? subjectId, int? offset, int? limit, ConsentService svc, TimeProvider clock) =>
        {
            var ahora = clock.GetUtcNow();
            return svc.ListForSubject(Ref.TryCreate(subjectKind, subjectId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<ConsentResponse>(
                    p.Items.Select(g => ConsentResponse.From(g, ahora)).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        // Consultar es POST porque el sujeto y el propósito son datos personales por su sola
        // combinación: "¿este paciente autorizó tratamiento oncológico?" no puede quedar en la
        // URL, donde vive en logs de proxy y en el historial.
        app.MapPost("/v1/grants/check", (RevokeRequest req, ConsentService svc, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");
            if (string.IsNullOrWhiteSpace(req.Purpose)) return Invalid("bad_purpose", "Hace falta el propósito.");

            return svc.Check(subject, req.Purpose!).Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToHttp();
        });

        app.MapPost("/v1/grants/revoke", (RevokeRequest req, ConsentService svc, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");
            if (string.IsNullOrWhiteSpace(req.Purpose)) return Invalid("bad_purpose", "Hace falta el propósito.");

            return svc.Revoke(subject, req.Purpose!).Map(g => ConsentResponse.From(g, clock.GetUtcNow())).ToHttp();
        });

        app.MapPost("/v1/grants/forget", (ForgetRequest req, ConsentService svc) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return Results.Ok(new ForgetResponse(svc.Forget(subject)));
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{ConsentRules.CodePrefix}.{code}", message).ToProblem();
}
