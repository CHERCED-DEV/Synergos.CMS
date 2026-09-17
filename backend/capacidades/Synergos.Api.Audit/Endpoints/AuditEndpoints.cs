using System.Globalization;
using Synergos.Api.Audit.Contracts;
using Synergos.Api.Audit.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Audit.Endpoints;

/// <summary>
/// El ruteo de la bitácora. <b>No hay PUT, PATCH ni DELETE — y acá no es solo convención.</b>
/// </summary>
/// <remarks>
/// En las demás capacidades, la ausencia de <c>PUT</c> es una decisión de diseño de API. Acá es
/// la propiedad entera: una bitácora que se puede editar o borrar no sirve de bitácora, porque
/// lo primero que hace quien quiere esconder algo es corregir el registro.
/// </remarks>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/v1/entries", (
            AppendEntryRequest req, HttpRequest http, AuditService svc,
            IdentityTokenGate identidad, TimeProvider clock) =>
        {
            if (!IdempotencyHeader.TryRead(http, AuditRules.CodePrefix, out var key, out var falta)) return falta!;

            var principal = Ref.TryCreate(req.ActorKind, req.ActorId);
            if (principal is null) return Invalid("bad_actor", "Hacen falta actorKind y actorId.");

            var target = Ref.TryCreate(req.TargetKind, req.TargetId);
            if (target is null) return Invalid("bad_target", "Hacen falta targetKind y targetId.");

            // QUIÉN ACTUÓ Y CON QUÉ ROLES NO SE LE CREE AL LLAMADOR SI TRAE CON QUÉ PROBARLO (#72).
            //
            // Antes salían del cuerpo y ya: con la llave compartida se escribía un asiento a
            // nombre de quien fuera, con los roles que fuera, y quedaba permanente. Es el defecto
            // #48 en la capacidad cuyo valor entero es ser creíble — y el más silencioso de los
            // tres, porque un asiento falso no falla: se guarda.
            //
            // Se parsea acá y no en el servicio para distinguir «no vino» de «vino un valor que
            // no existe»: las dos van al mismo rechazo, con detalle distinto.
            IdentityAssertion? declarada = Enum.TryParse<IdentityAssertion>(req.Assertion, ignoreCase: true, out var a)
                ? a
                : null;

            var quien = IdentityAssertions.ResolveActor(
                identidad, http.Headers[IdentityTokens.HeaderName].FirstOrDefault(),
                principal, req.ActorRoles, declarada, clock.GetUtcNow(), AuditRules.CodePrefix);

            if (!quien.IsOk) return quien.Rejection!.ToProblem();

            return svc.Append(quien.Value.Actor, req.Action, target, req.Details, key, quien.Value.Assertion).Match(
                e => Results.Created($"/v1/entries/{e.Id}", AuditEntryResponse.From(e)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/entries/{id}", (string id, AuditService svc) =>
            svc.Get(id).Map(AuditEntryResponse.From).ToHttp());

        app.MapGet("/v1/entries", (string? targetKind, string? targetId, string? actorId,
            string? from, string? to, int? offset, int? limit, AuditService svc, TimeProvider clock) =>
        {
            if (!TryInstant(from, out var desde)) return Invalid("bad_from", $"'{from}' no es una fecha ISO-8601.");
            if (!TryInstant(to, out var hasta)) return Invalid("bad_to", $"'{to}' no es una fecha ISO-8601.");

            var ahora = clock.GetUtcNow();
            var inicio = desde ?? ahora.AddDays(-QueryWindow.DefaultDays);
            var fin = hasta ?? ahora;
            if (fin <= inicio) return Invalid("bad_window", $"La ventana no avanza: {inicio:O} → {fin:O}.");

            var target = Ref.TryCreate(targetKind, targetId);

            return svc.Query(target, actorId, TimeWindow.Of(inicio, fin),
                    Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => new PageResponse<AuditEntryResponse>(
                    p.Items.Select(AuditEntryResponse.From).ToList(), p.Total, p.Offset, p.HasMore))
                .ToHttp();
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{AuditRules.CodePrefix}.{code}", message).ToProblem();

    private static bool TryInstant(string? raw, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;
        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var p)) return false;
        value = p;
        return true;
    }
}
