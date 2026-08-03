using Synergos.Api.Signing.Contracts;
using Synergos.Api.Signing.Domain;
using Synergos.Shared;

namespace Synergos.Api.Signing.Endpoints;

/// <summary>El ruteo de la firma.</summary>
public static class SigningEndpoints
{
    public static IEndpointRouteBuilder MapSigningEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/keys", (CreateKeyRequest req, HttpRequest http, SigningService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, SigningRules.CodePrefix, out var key, out var falta)) return falta!;

            return svc.CreateKey(req.Purpose, key).Match(
                k => Results.Created($"/v1/keys/{k.Id}", KeyResponse.From(k)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/keys", (string? purpose, int? offset, int? limit, SigningService svc) =>
            svc.ListForPurpose(purpose, Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<KeyResponse>(
                    p.Items.Select(KeyResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp());

        app.MapPost("/v1/keys/{id}/retire", (string id, SigningService svc) =>
            svc.RetireKey(id).Map(KeyResponse.From).ToHttp());

        // Firmar y verificar son POST aunque verificar no mute: el token es material sensible y
        // en la URL queda en logs de proxy, en el historial y en el Referer.
        app.MapPost("/v1/signatures", (SignRequest req, SigningService svc) =>
        {
            var vigencia = TimeSpan.FromMinutes(req.LifetimeMinutes ?? 60);

            return svc.Sign(req.Purpose, req.Payload, vigencia).Match(
                s => Results.Ok(new SignatureResponse(s.Token, s.ExpiresAt)),
                bad => bad.ToProblem());
        });

        app.MapPost("/v1/signatures/verify", (VerifyRequest req, SigningService svc) =>
            svc.Verify(req.Token).Map(p => new VerifiedResponse(p)).ToHttp());

        return app;
    }
}
