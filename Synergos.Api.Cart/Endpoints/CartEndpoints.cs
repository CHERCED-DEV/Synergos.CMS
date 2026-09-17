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
        // ABRIR UNA CANASTA NOMBRA A UNA PERSONA, así que se comprueba (HU #14).
        //
        // Es el único endpoint de esta capacidad en el que el llamador NOMBRA al dueño; los demás
        // llegan con el identificador de una canasta que ya sabe de quién es. Hasta acá, quien
        // tuviera la llave compartida abría canastas a nombre de cualquiera y nadie podía notarlo
        // después: es el defecto #42 sobre el dato que decide de quién es lo que se va a comprar.
        app.MapPost("/v1/carts", (
            OpenCartRequest req, HttpRequest http, CartService svc,
            IdentityTokenGate identidad, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, CartRules.CodePrefix, out var key, out var falta)) return falta!;

            var owner = Ref.TryCreate(req.OwnerKind, req.OwnerId);
            if (owner is null) return Invalid("bad_owner", "Hacen falta ownerKind y ownerId.");

            var (assertion, motivo) = Afirmacion(identidad, http, owner, req.Assertion, clock);
            if (assertion is null) return motivo!.ToProblem();

            var ttl = req.TtlHours is { } h ? TimeSpan.FromHours(h) : (TimeSpan?)null;

            return svc.Open(owner, ttl, key, assertion.Value).Match(
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

        // LO QUE SIGUE NO NOMBRA A NADIE, y por eso no resuelve identidad.
        //
        // Estos tres llegan con el id de una canasta que YA sabe de quién es: lo que el cuerpo
        // nombra —`subjectKind`/`subjectId`— es el producto, el OBJETO de la operación, no quien
        // la hace. Exigirles una afirmación no añadiría ninguna prueba; sólo movería el campo de
        // sitio. Que alguien con la llave compartida pueda tocar la canasta de otro si adivina su
        // identificador es una pregunta DISTINTA —autorizar, no atribuir— y esta HU no la
        // contesta en ninguna de las capacidades donde la ha resuelto (`CLAUDE.md` §11).
        //
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

    /// <summary>
    /// Con qué fuerza se afirmó la identidad del dueño — <b>lo decide ESTA capacidad</b>.
    /// </summary>
    /// <remarks>
    /// <para><b>No se cree lo declarado</b> (HU #14, defecto #42). Si presenta token, se verifica
    /// en local y se comprueba que su sujeto sea el mismo <paramref name="who"/>: sin eso el token
    /// sería decoración y la capacidad seguiría creyendo el dueño que le mandan. Si no presenta,
    /// lo más fuerte que se le acepta es <c>CmsSession</c>, que es honesto porque significa «nos
    /// fiamos de quien llama».</para>
    ///
    /// <para><b>Verificación LOCAL</b>, sin llamar a <c>Api.Identity</c>: llamarla en cada
    /// petición la convertiría en el punto único de fallo de las veinte — y sería además una
    /// flecha capacidad→capacidad, prohibida de plano y con gate (#49).</para>
    ///
    /// <para><b>Y la regla no se reimplementa acá</b>: vive en <c>Synergos.Shared</c> porque la
    /// comparten las capacidades que aceptan identidad, y una copia local se desviaría de la
    /// original en silencio — las dos compilan.</para>
    /// </remarks>
    private static (IdentityAssertion? Assertion, Rejection? Rejection) Afirmacion(
        IdentityTokenGate identidad, HttpRequest http, Ref who, string? declarada, TimeProvider clock)
    {
        // Se parsea acá y no en el servicio para poder distinguir «no vino» de «vino un valor que
        // no existe» — las dos van al mismo rechazo, pero con detalle distinto.
        IdentityAssertion? afirmada = Enum.TryParse<IdentityAssertion>(declarada, ignoreCase: true, out var a)
            ? a
            : null;

        return IdentityAssertions.Resolve(
            identidad,
            http.Headers[IdentityTokens.HeaderName].FirstOrDefault(),
            who,
            afirmada,
            clock.GetUtcNow(),
            CartRules.CodePrefix);
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{CartRules.CodePrefix}.{code}", message).ToProblem();
}
