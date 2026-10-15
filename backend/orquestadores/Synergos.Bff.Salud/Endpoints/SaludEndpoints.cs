using Microsoft.AspNetCore.Http.HttpResults;
using Synergos.Bff.Core;
using Synergos.Bff.Salud.Contracts;
using Synergos.Bff.Salud.Domain;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Bff.Salud.Endpoints;

/// <summary>El ruteo del orquestador de Salud.</summary>
/// <remarks>
/// Cada endpoint declara su respuesta con el TIPO DE RETORNO y su nombre con <c>WithName</c> (el molde F2):
/// de ahí sale el contrato publicado (<c>docs/contracts/openapi/Synergos.Bff.Salud.json</c>), contra el que el
/// CMS cruza lo que lee (<c>HttpClinicalSchedulingService</c>). Son las rutas de la cita imperativa
/// (<see cref="AppointmentFlow"/>), las que usa la ruta directa del CMS; las de la puerta las pone el flujo
/// declarado (<c>flujos/salud.cita.json</c>) en <c>/v1/flujos/salud.cita</c>.
/// </remarks>
public static class SaludEndpoints
{
    /// <summary>Prefijo de los códigos de rechazo propios del BFF.</summary>
    public const string CodePrefix = "salud";

    public static IEndpointRouteBuilder MapSaludEndpoints(this IEndpointRouteBuilder app)
    {
        // La llave de idempotencia ES el identificador de la saga. No es un atajo: la saga
        // necesita un identificador estable ANTES del primer paso para poder derivar las llaves
        // de todos los demás, y el llamador ya está obligado a traer uno.
        // Por eso acepta una llave más corta que la de una capacidad: de ella cuelgan las de cada
        // paso, y una que no cupiera rompería a mitad de la saga (ConLlaveDeSaga).
        app.MapPost("/v1/appointments", async Task<Results<Created<AppointmentResponse>, ProblemHttpResult>> (
            ScheduleRequest req, HttpRequest http, AppointmentFlow flow, CancellationToken ct) =>
        {
            if (!IdempotencyHeader.TryRead(http, CodePrefix, out var key, out var falta)) return falta!;

            var patient = Ref.TryCreate(req.PatientKind, req.PatientId);
            if (patient is null) return Invalid("bad_patient", "Hacen falta patientKind y patientId.");
            var professional = Ref.TryCreate(req.ProfessionalKind, req.ProfessionalId);
            if (professional is null) return Invalid("bad_professional", "Hacen falta professionalKind y professionalId.");
            var service = Ref.TryCreate(req.ServiceKind, req.ServiceId);
            if (service is null) return Invalid("bad_service", "Hacen falta serviceKind y serviceId.");
            // resourceId es OPCIONAL: si no viene, el flujo lo resuelve desde el profesional.
            // Exigirlo obligaba a que el identificador interno de Api.Booking viajara hasta el
            // CMS, que es justo lo que este contrato dice que no debe pasar (HU #25).
            if (req.Start is null || req.End is null || req.End <= req.Start)
            {
                return Invalid("bad_window", "Hacen falta start y end, y end tiene que ser posterior.");
            }

            var r = await flow.ScheduleAsync(patient, professional, req.ResourceId, service,
                TimeWindow.Of(req.Start.Value, req.End.Value), key.Value, ct);

            return r.Map(AppointmentResponse.From).ToCreated(s => $"/v1/appointments/{s.Id}");
        }).WithName("ScheduleAppointment").ConLlaveDeSaga();

        app.MapGet("/v1/appointments/{id}", Results<Ok<AppointmentResponse>, ProblemHttpResult> (string id, AppointmentFlow flow) =>
            flow.Get(id).Map(AppointmentResponse.From).ToHttp()).WithName("GetAppointment");

        app.MapPost("/v1/appointments/{id}/confirm", async Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> (
            string id, AppointmentFlow flow, CancellationToken ct) =>
            (await flow.ConfirmAsync(id, ct)).Map(AppointmentResponse.From).ToHttp()).WithName("ConfirmAppointment");

        app.MapPost("/v1/appointments/{id}/cancel", async Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> (
            string id, AppointmentFlow flow, CancellationToken ct) =>
            (await flow.CancelAsync(id, ct)).Map(AppointmentResponse.From).ToHttp()).WithName("CancelAppointment");

        // Volver a intentar lo que se rindió. Es la puerta de la persona a la que se le avisó:
        // sin ella, "se rinde a los ocho intentos" sería "se abandona", y arreglar una devolución
        // colgada exigiría tocarla a mano en la capacidad, por fuera del rastro de la saga.
        app.MapPost("/v1/appointments/{id}/retry", async Task<Results<Ok<AppointmentResponse>, ProblemHttpResult>> (
            string id, AppointmentFlow flow, CancellationToken ct) =>
            (await flow.RetryStuckAsync(id, ct)).Map(AppointmentResponse.From).ToHttp()).WithName("RetryAppointment");

        // Lo que cuesta agendar, ANTES de agendar: la pantalla del paciente lo muestra con la misma
        // regla con que la cita se autoriza (CMS#196). No choca con GET /v1/appointments/{id}: en el
        // enrutado de ASP.NET un segmento literal gana a un parámetro, sin importar el orden.
        app.MapGet("/v1/appointments/quote", async Task<Results<Ok<MoneyDto>, ProblemHttpResult>> (
            string? serviceKind, string? serviceId, AppointmentFlow flow, CancellationToken ct) =>
        {
            var service = Ref.TryCreate(serviceKind, serviceId);
            if (service is null) return Invalid("bad_service", "Hacen falta serviceKind y serviceId.");

            return (await flow.CopagoAsync(service, ct)).Map(c => new MoneyDto(c.Amount, c.Currency)).ToHttp();
        }).WithName("QuoteAppointment");

        // La vista de operación: qué quedó colgado. Sin ella, una compensación que se rindió
        // solo existe en una línea de log que nadie está mirando.
        app.MapGet("/v1/compensations", Ok<PageResponse<PendingCompensationResponse>> (int? offset, int? limit, AppointmentFlow flow) =>
        {
            var todas = flow.PendingCompensations()
                .SelectMany(s => s.Pending().Select(c => new PendingCompensationResponse(
                    s.Id, c.Kind, c.Reason, c.Attempts, c.NextAttemptUtc, c.LastError,
                    c.IsStuck, s.AlertedAtUtc)))
                .ToList();

            var off = Math.Max(0, offset ?? 0);
            var lim = QueryWindow.Limit(limit);
            return TypedResults.Ok(new PageResponse<PendingCompensationResponse>(
                todas.Skip(off).Take(lim).ToList(), todas.Count, off, off + lim < todas.Count));
        }).WithName("ListAppointmentCompensations");

        return app;
    }

    private static ProblemHttpResult Invalid(string code, string message)
        => Rejection.Invalid($"{CodePrefix}.{code}", message).ToProblem();
}
