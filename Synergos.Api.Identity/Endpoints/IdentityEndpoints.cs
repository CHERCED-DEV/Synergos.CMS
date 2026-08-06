using Microsoft.Extensions.Options;
using Synergos.Api.Identity.Contracts;
using Synergos.Api.Identity.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Identity.Endpoints;

/// <summary>El ruteo de la identidad.</summary>
public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/principals", (RegisterPrincipalRequest req, HttpRequest http, IdentityService svc, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, IdentityRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.Register(subject, req.Secret, req.Roles ?? Array.Empty<string>(), key).Match(
                p => Results.Created($"/v1/principals/{p.Id}", PrincipalResponse.From(p, clock.GetUtcNow())),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/principals/{id}", (string id, IdentityService svc, TimeProvider clock) =>
            svc.Get(id).Map(p => PrincipalResponse.From(p, clock.GetUtcNow())).ToHttp());

        app.MapGet("/v1/principals", (int? offset, int? limit, IdentityService svc, TimeProvider clock) =>
        {
            var ahora = clock.GetUtcNow();
            var page = svc.List(Math.Max(0, offset ?? 0), QueryWindow.Limit(limit));
            return Results.Ok(new PageResponse<PrincipalResponse>(
                page.Items.Select(p => PrincipalResponse.From(p, ahora)).ToList(), page.Total, page.Offset, page.HasMore));
        });

        // ── Tokens de identidad (HU #14) ────────────────────────────────────
        //
        // Emitir es un acto de esta capacidad y NO del que pregunta: si firmara el llamador, la
        // afirmación diría «nuestro propio BFF dice que sí», que es literalmente lo que
        // CmsSession ya dice. Que el emisor no sea el que pregunta es lo único que hace que el
        // escalón exista.
        app.MapPost("/v1/tokens", (IssueTokenRequest req, IdentityService svc, IdentityTokens tokens,
            IOptions<TokenOptions> opciones, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            var o = opciones.Value;
            return svc.IssueToken(subject, o.LifetimeMinutes)
                .Map(c => new TokenResponse(
                    tokens.Issue(c), c.ExpiresAtUtc, c.SessionStartedAtUtc.AddMinutes(o.MaxSessionMinutes)))
                .ToHttp();
        });

        app.MapPost("/v1/tokens/renew", (RenewTokenRequest req, IdentityService svc, IdentityTokens tokens,
            IOptions<TokenOptions> opciones) =>
        {
            var o = opciones.Value;
            return svc.RenewToken(tokens, req.Token, o.LifetimeMinutes, o.MaxSessionMinutes)
                .Map(c => new TokenResponse(
                    tokens.Issue(c), c.ExpiresAtUtc, c.SessionStartedAtUtc.AddMinutes(o.MaxSessionMinutes)))
                .ToHttp();
        });

        // Verificar una credencial es un POST y no un GET: el secreto no puede viajar en la URL,
        // donde queda en logs de proxy, en el historial y en el Referer.
        app.MapPost("/v1/credentials/verify", (VerifyCredentialRequest req, IdentityService svc) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.Authenticate(subject, req.Secret).Map(ActorResponse.From).ToHttp();
        });

        app.MapPost("/v1/principals/{id}/roles/grant", (string id, RolesRequest req, IdentityService svc, TimeProvider clock) =>
            svc.GrantRoles(id, req.Roles ?? Array.Empty<string>()).Map(p => PrincipalResponse.From(p, clock.GetUtcNow())).ToHttp());

        app.MapPost("/v1/principals/{id}/roles/revoke", (string id, RolesRequest req, IdentityService svc, TimeProvider clock) =>
            svc.RevokeRoles(id, req.Roles ?? Array.Empty<string>()).Map(p => PrincipalResponse.From(p, clock.GetUtcNow())).ToHttp());

        app.MapPost("/v1/principals/{id}/unlock", (string id, IdentityService svc, TimeProvider clock) =>
            svc.Unlock(id).Map(p => PrincipalResponse.From(p, clock.GetUtcNow())).ToHttp());

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{IdentityRules.CodePrefix}.{code}", message).ToProblem();
}
