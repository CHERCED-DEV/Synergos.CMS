using Synergos.Api.Cart.Contracts;
using Synergos.Api.Cart.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Cart.Endpoints;

/// <summary>El ruteo de la canasta.</summary>
public static class CartEndpoints
{
    public static IEndpointRouteBuilder MapCartEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/carts", (OpenCartRequest req, HttpRequest http, CartService svc, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, CartRules.CodePrefix, out var key, out var falta)) return falta!;

            var owner = Ref.TryCreate(req.OwnerKind, req.OwnerId);
            if (owner is null) return Invalid("bad_owner", "Hacen falta ownerKind y ownerId.");

            var ttl = req.TtlHours is { } h ? TimeSpan.FromHours(h) : (TimeSpan?)null;

            return svc.Open(owner, ttl, key).Match(
                c => Results.Created($"/v1/carts/{c.Id}", CartResponse.From(c, clock.GetUtcNow())),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/carts/{id}", (string id, CartService svc, TimeProvider clock) =>
            svc.Get(id).Map(c => CartResponse.From(c, clock.GetUtcNow())).ToHttp());

        app.MapGet("/v1/carts", (string? ownerKind, string? ownerId, int? offset, int? limit, CartService svc, TimeProvider clock) =>
        {
            var ahora = clock.GetUtcNow();
            return svc.ListForOwner(Ref.TryCreate(ownerKind, ownerId), Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<CartResponse>(
                    p.Items.Select(c => CartResponse.From(c, ahora)).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        // Poner una línea REEMPLAZA la cantidad si el sujeto ya estaba. Es lo que hace que un
        // reintento tras un timeout no duplique: con semántica de suma, el cliente termina con
        // seis unidades de algo que pidió dos veces. Por eso tampoco lleva Idempotency-Key:
        // la operación ya es idempotente por diseño.
        app.MapPost("/v1/carts/{id}/lines", (string id, CartLineRequest req, CartService svc, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.SetLine(id, subject, req.Quantity ?? 0).Map(c => CartResponse.From(c, clock.GetUtcNow())).ToHttp();
        });

        app.MapPost("/v1/carts/{id}/lines/remove", (string id, CartLineRequest req, CartService svc, TimeProvider clock) =>
        {
            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            return svc.RemoveLine(id, subject).Map(c => CartResponse.From(c, clock.GetUtcNow())).ToHttp();
        });

        app.MapPost("/v1/carts/{id}/checkout", (string id, CartService svc, TimeProvider clock) =>
            svc.CheckOut(id).Map(c => CartResponse.From(c, clock.GetUtcNow())).ToHttp());

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{CartRules.CodePrefix}.{code}", message).ToProblem();
}
