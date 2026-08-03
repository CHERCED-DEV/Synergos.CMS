using Synergos.Bff.Core;
using Synergos.Bff.Salud.Contracts;
using Synergos.Bff.Salud.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Salud.Endpoints;

/// <summary>El ruteo del orquestador de Salud.</summary>
public static class SaludEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "salud";

    public static IEndpointRouteBuilder MapSaludEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        app.MapPost("/v1/appointments", async (ScheduleRequest req, HttpRequest http, AppointmentFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            var patient = Ref.TryCreate(req.PatientKind, req.PatientId);
            if (patient is null) return Invalid("bad_patient", "Hacen falta patientKind y patientId.");
            var professional = Ref.TryCreate(req.ProfessionalKind, req.ProfessionalId);
            if (professional is null) return Invalid("bad_professional", "Hacen falta professionalKind y professionalId.");
            var service = Ref.TryCreate(req.ServiceKind, req.ServiceId);
            if (service is null) return Invalid("bad_service", "Hacen falta serviceKind y serviceId.");
            if (string.IsNullOrWhiteSpace(req.ResourceId)) return Invalid("bad_resource", "Hace falta resourceId.");
            if (req.Start is null || req.End is null || req.End <= req.Start)
            {
                return Invalid("bad_window", "Hacen falta start y end, y end tiene que ser posterior.");
            }

            var r = await flow.ScheduleAsync(patient, professional, req.ResourceId!, service,
                TimeWindow.Of(req.Start.Value, req.End.Value), key.Value, ct);

            return r.Match(
                s => Results.Created($"/v1/appointments/{s.Id}", AppointmentResponse.From(s)),
                bad => bad.ToProblem());
        });

        app.MapGet("/v1/appointments/{id}", (string id, AppointmentFlow flow) =>
            flow.Get(id).Map(AppointmentResponse.From).ToHttp());

        app.MapPost("/v1/appointments/{id}/confirm", async (string id, AppointmentFlow flow, CancellationToken ct) =>
            (await flow.ConfirmAsync(id, ct)).Map(AppointmentResponse.From).ToHttp());

        app.MapPost("/v1/appointments/{id}/cancel", async (string id, AppointmentFlow flow, CancellationToken ct) =>
            (await flow.CancelAsync(id, ct)).Map(AppointmentResponse.From).ToHttp());

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, "se rinde a los ocho intentos" sería "se abandona", y arreglar una devolución
        // colgada exigiría tocarla a mano en la capacidad, por fuera del rastro de la saga.
        app.MapPost("/v1/appointments/{id}/retry", async (string id, AppointmentFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(AppointmentResponse.From).ToHttp());

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió
        // solo existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", (int? offset, int? limit, AppointmentFlow flow) =>
        {
            var todas = flow.PendingCompensations()
                .SelectMany(s => s.Pending().Select(c => new PendingCompensationResponse(
                    s.Id, c.Kind, c.Reason, c.Attempts, c.NextAttemptUtc, c.LastError,
                    c.IsStuck, s.AlertedAtUtc)))
                .ToList();

            var off = Math.Max(0, offset ?? 0);
            var lim = QueryWindow.Limit(limit);
            return Results.Ok(new PageResponse<PendingCompensationResponse>(
                todas.Skip(off).Take(lim).ToList(), todas.Count, off, off + lim < todas.Count));
        });

        return app;
    }

    private static IResult Invalid(string code, string message)
        => Rejection.Invalid($"{CodePrefix}.{code}", message).ToProblem();
}
