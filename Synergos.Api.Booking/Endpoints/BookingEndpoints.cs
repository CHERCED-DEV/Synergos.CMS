using System.Globalization;
using Synergos.Api.Booking.Contracts;
using Synergos.Api.Booking.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Booking.Endpoints;

/// <summary>
/// El ruteo de la capacidad. Delgado a propósito: valida el borde, delega, y desempaqueta.
/// </summary>
/// <remarks>
/// <para><b>Las tres cosas que hace un endpoint y ninguna más</b> (doc 08 §4.4): traduce el
/// contrato a tipos del dominio devolviendo <c>Rejection.Invalid</c> si no puede —nunca lanza,
/// porque una excepción es un 500 y un 500 le dice al cliente "reintentá" cuando la respuesta
/// correcta es "arreglá lo que mandaste"—, llama al servicio, y desempaqueta el
/// <c>Result</c> con <see cref="RejectionResults.ToHttp"/>.</para>
///
/// <para><b>Sin <c>PUT</c> ni <c>PATCH</c>.</b> Las transiciones son acciones con nombre:
/// <c>POST /holds/{id}/confirm</c> dice qué pasó. Un <c>PATCH {"status":"confirmed"}</c> deja
/// que el cliente invente transiciones que la capacidad tendría que ir rechazando de a una.</para>
/// </remarks>
public static class BookingEndpoints
{
    public static IEndpointRouteBuilder MapBookingEndpoints(this IEndpointRouteBuilder app)
    {
        // ── Recursos ────────────────────────────────────────────────────────
        app.MapPost("/v1/resources", (CreateResourceRequest req, HttpRequest http, BookingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, BookingRules.CodePrefix, out var key, out var falta)) return falta!;

            var subject = Ref.TryCreate(req.SubjectKind, req.SubjectId);
            if (subject is null) return Invalid("bad_subject", "Hacen falta subjectKind y subjectId.");

            var opening = new List<OpeningRule>();
            foreach (var o in req.Opening ?? Array.Empty<OpeningRuleDto>())
            {
                if (!Enum.TryParse<DayOfWeek>(o.Day, ignoreCase: true, out var day))
                    return Invalid("bad_opening_day", $"'{o.Day}' no es un día de la semana.");
                if (!TimeOnly.TryParse(o.From, out var from) || !TimeOnly.TryParse(o.To, out var to))
                    return Invalid("bad_opening_time", $"'{o.From}'–'{o.To}' no es un rango horario.");
                if (to <= from)
                    return Invalid("bad_opening_range", $"La franja {o.From}–{o.To} no avanza.");
                opening.Add(new OpeningRule(day, from, to));
            }

            var policy = new CancellationPolicy(TimeSpan.FromMinutes(Math.Max(0, req.CancellationNoticeMinutes ?? 0)));
            var res = svc.RegisterResource(subject, req.Capacity ?? 1, req.TimeZoneId ?? "UTC", opening, policy, key);

            return res.Match(
                r => Results.Created($"/v1/resources/{r.Id}", ResourceResponse.From(r)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/resources/{id}", (string id, BookingService svc) =>
            svc.GetResource(id).Map(ResourceResponse.From).ToHttp());

        // Con subject se resuelve UNO; sin él se lista la página. Es la misma forma que
        // Api.Inventory le dio a /v1/items, y por la misma razón: quien tiene la referencia de su
        // entidad —un profesional, una sala— no tiene el identificador interno que esta capacidad
        // genera, y obligarlo a llevar un mapa aparte crea una segunda verdad.
        app.MapGet("/v1/resources", (string? subjectKind, string? subjectId, int? offset, int? limit, BookingService svc) =>
        {
            if (subjectKind is not null || subjectId is not null)
            {
                var subject = Ref.TryCreate(subjectKind, subjectId);
                return subject is null
                    ? Invalid("bad_subject", "Para buscar por sujeto hacen falta subjectKind y subjectId.")
                    : svc.GetResourceBySubject(subject).Map(ResourceResponse.From).ToHttp();
            }

            var page = svc.ListResources(Math.Max(0, offset ?? 0), QueryWindow.Limit(limit));
            return Results.Ok(ToPage(page.Map(ResourceResponse.From)));
        });

        // start y end llegan como TEXTO y se parsean acá, en vez de dejar que los ate el binder
        // a DateTimeOffset?. Con el binder, una fecha mal formada produce un 400 GENÉRICO y con
        // cuerpo vacío —el llamador no sabe cuál de los dos campos era ni por qué—, y el molde
        // dice que el borde valida devolviendo un Rejection con código (doc 08 §4.4).
        app.MapGet("/v1/resources/{id}/availability", (string id, string? start, string? end, BookingService svc) =>
        {
            if (!TryInstant(start, out var desde)) return Invalid("bad_start", $"'{start}' no es una fecha ISO-8601.");
            if (!TryInstant(end, out var hasta)) return Invalid("bad_end", $"'{end}' no es una fecha ISO-8601.");

            var window = TryWindow(desde, hasta);
            if (window is null) return Invalid("bad_window", "Hacen falta start y end, y end tiene que ser posterior.");

            return svc.CheckAvailability(id, window.Value)
                .Map(a => new AvailabilityResponse(a.Available, a.Taken, a.Capacity, a.Reason?.Code, a.Reason?.Message))
                .ToHttp();
        });

        // ── Holds ───────────────────────────────────────────────────────────
        app.MapPost("/v1/holds", (CreateHoldRequest req, HttpRequest http, BookingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, BookingRules.CodePrefix, out var key, out var falta)) return falta!;

            if (string.IsNullOrWhiteSpace(req.ResourceId)) return Invalid("bad_resource_id", "Hace falta resourceId.");

            var heldFor = Ref.TryCreate(req.HeldForKind, req.HeldForId);
            if (heldFor is null) return Invalid("bad_held_for", "Hacen falta heldForKind y heldForId.");

            var window = TryWindow(req.Start, req.End);
            if (window is null) return Invalid("bad_window", "Hacen falta start y end, y end tiene que ser posterior.");

            var ttl = req.TtlMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;
            var res = svc.CreateHold(req.ResourceId!, window.Value, heldFor, ttl, key);

            return res.Match(
                h => Results.Created($"/v1/holds/{h.Id}", HoldResponse.From(h)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/holds/{id}", (string id, BookingService svc) =>
            svc.GetHold(id).Map(HoldResponse.From).ToHttp());

        app.MapPost("/v1/holds/{id}/release", (string id, BookingService svc) =>
            svc.ReleaseHold(id).Map(HoldResponse.From).ToHttp());

        app.MapPost("/v1/holds/{id}/confirm", (string id, HttpRequest http, BookingService svc) =>
        {
            if (!IdempotencyHeader.TryRead(http, BookingRules.CodePrefix, out var key, out var falta)) return falta!;

            return svc.ConfirmHold(id, key).Match(
                r => Results.Created($"/v1/reservations/{r.Id}", ReservationResponse.From(r)),
                bad => bad.ToProblem());
        });

        // ── Reservas ────────────────────────────────────────────────────────
        app.MapGet("/v1/reservations/{id}", (string id, BookingService svc) =>
            svc.GetReservation(id).Map(ReservationResponse.From).ToHttp());

        app.MapGet("/v1/reservations", (string? resourceId, int? offset, int? limit, BookingService svc) =>
        {
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                // Sin filtro, esto sería un volcado del almacén entero por HTTP.
                return Invalid("resource_id_required", "Hace falta resourceId.");
            }

            return svc.ListReservations(resourceId!, Math.Max(0, offset ?? 0), QueryWindow.Limit(limit))
                .Map(p => ToPage(p.Map(ReservationResponse.From)))
                .ToHttp();
        });

        app.MapPost("/v1/reservations/{id}/cancel", (string id, BookingService svc) =>
            svc.CancelReservation(id).Map(ReservationResponse.From).ToHttp());

        return app;
    }

    // ── Traducción del borde ────────────────────────────────────────────────

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{BookingRules.CodePrefix}.{code}", message).ToProblem();

    /// <summary>
    /// Lee un instante ISO-8601 de la query. Ausente es válido (lo resuelve el llamador).
    /// </summary>
    /// <remarks>
    /// <see cref="DateTimeStyles.RoundtripKind"/> para que un <c>Z</c> o un <c>+00:00</c> no se
    /// reinterpreten en la hora local del servidor: una agenda que corre en otro huso que el
    /// cliente devolvería disponibilidad de otro momento del día sin decirlo.
    /// </remarks>
    private static bool TryInstant(string? raw, out DateTimeOffset? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw)) return true;

        if (!DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            return false;
        }
        value = parsed;
        return true;
    }

    private static TimeWindow? TryWindow(DateTimeOffset? start, DateTimeOffset? end)
        => start is null || end is null || end <= start ? null : TimeWindow.Of(start.Value, end.Value);

    private static PageResponse<T> ToPage<T>(Page<T> page)
        => new(page.Items, page.Total, page.Offset, page.HasMore);
}
