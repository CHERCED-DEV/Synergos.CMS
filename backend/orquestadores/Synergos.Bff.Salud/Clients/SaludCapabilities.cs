using Synergos.Bff.Core;
using Synergos.Core;

namespace Synergos.Bff.Salud.Clients;

/// <summary>Las capacidades que este BFF necesita, cada una tras su cliente HTTP nombrado.</summary>
/// <remarks>
/// <b>Un cliente nombrado por capacidad</b> y no uno genérico: cada una tiene su URL base, su
/// llave y su timeout, y mezclarlas haría que subir el timeout de Payments se lo subiera también
/// a Consent.
/// </remarks>
public sealed class SaludCapabilities : CapabilityClients
{
    public const string Consent = "consent";
    public const string Booking = "booking";
    public const string Pricing = "pricing";
    public const string Payments = "payments";
    public const string Workflow = "workflow";
    public const string Signing = "signing";

    public SaludCapabilities(IHttpClientFactory clients) : base(clients) { }

    // ── Consent ─────────────────────────────────────────────────────────────

    public Task<Result<ConsentDto>> CheckConsentAsync(Ref patient, string purpose, CancellationToken ct)
        => Post<ConsentDto>(Consent, "v1/grants/check",
            new { subjectKind = patient.Kind, subjectId = patient.Id, purpose }, null, ct);

    // ── Booking ─────────────────────────────────────────────────────────────

    public Task<Result<ResourceDto>> FindResourceAsync(string resourceId, CancellationToken ct)
        => Get<ResourceDto>(Booking, $"v1/resources/{resourceId}", ct);

    /// <summary>Del <see cref="Ref"/> del profesional a su agenda en <c>Api.Booking</c>.</summary>
    /// <remarks>
    /// Es lo que evita que el identificador INTERNO de la capacidad tenga que viajar hacia arriba
    /// hasta el CMS. Este contrato ya dice que los identificadores internos de cada capacidad no
    /// tienen por qué salir a la UI; exigirlos de ENTRADA era la misma fuga al revés.
    /// </remarks>
    public Task<Result<ResourceDto>> FindResourceBySubjectAsync(Ref subject, CancellationToken ct)
        => Get<ResourceDto>(Booking,
            $"v1/resources?subjectKind={Uri.EscapeDataString(subject.Kind)}&subjectId={Uri.EscapeDataString(subject.Id)}", ct);

    public Task<Result<HoldDto>> HoldAsync(string resourceId, TimeWindow window, Ref patient, IdempotencyKey key, CancellationToken ct)
        => Post<HoldDto>(Booking, "v1/holds", new
        {
            resourceId,
            start = window.Start,
            end = window.End,
            heldForKind = patient.Kind,
            heldForId = patient.Id,
        }, key, ct);

    public Task<Result<ReservationDto>> ConfirmHoldAsync(string holdId, IdempotencyKey key, CancellationToken ct)
        => Post<ReservationDto>(Booking, $"v1/holds/{holdId}/confirm", null, key, ct);

    public Task<Result<HoldDto>> ReleaseHoldAsync(string holdId, CancellationToken ct)
        => Post<HoldDto>(Booking, $"v1/holds/{holdId}/release", null, null, ct);

    // ── Pricing ─────────────────────────────────────────────────────────────

    public Task<Result<QuoteDto>> QuoteAsync(Ref service, CancellationToken ct)
        => Post<QuoteDto>(Pricing, "v1/quotes", new
        {
            lines = new[] { new { subjectKind = service.Kind, subjectId = service.Id, quantity = 1 } },
        }, null, ct);

    // ── Payments ────────────────────────────────────────────────────────────

    public Task<Result<PaymentDto>> AuthorizeAsync(Ref forWhat, Ref payer, Money amount, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, "v1/payments", new
        {
            forKind = forWhat.Kind,
            forId = forWhat.Id,
            payerKind = payer.Kind,
            payerId = payer.Id,
            amount = new { amount = amount.Amount, currency = amount.Currency },
        }, key, ct);

    public Task<Result<PaymentDto>> CaptureAsync(string paymentId, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/capture", null, key, ct);

    public Task<Result<PaymentDto>> VoidAsync(string paymentId, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/void", null, null, ct);

    public Task<Result<PaymentDto>> RefundAsync(string paymentId, Money amount, string reason, IdempotencyKey key, CancellationToken ct)
        => Post<PaymentDto>(Payments, $"v1/payments/{paymentId}/refund", new
        {
            amount = new { amount = amount.Amount, currency = amount.Currency },
            reason,
        }, key, ct);

    public Task<Result<PaymentDto>> GetPaymentAsync(string paymentId, CancellationToken ct)
        => Get<PaymentDto>(Payments, $"v1/payments/{paymentId}", ct);

    // ── La consulta (I3): la agenda del día, la llegada y el sello de la nota ─────────────────

    /// <summary>Las reservas de un recurso, una página. Booking no filtra por fecha: quien llama recorta la ventana.</summary>
    public Task<Result<PaginaDto<ReservaDeLaAgendaDto>>> ReservasDelRecursoAsync(string recursoId, int offset, int limit, CancellationToken ct)
        => Get<PaginaDto<ReservaDeLaAgendaDto>>(Booking,
            $"v1/reservations?resourceId={Uri.EscapeDataString(recursoId)}&offset={offset}&limit={limit}", ct);

    /// <summary>Las instancias de un sujeto en Workflow: la de la llegada de una cita, si ya arrancó.</summary>
    public Task<Result<PaginaDto<InstanciaDeLlegadaDto>>> InstanciasDeAsync(Ref sujeto, CancellationToken ct)
        => Get<PaginaDto<InstanciaDeLlegadaDto>>(Workflow,
            $"v1/instances?subjectKind={Uri.EscapeDataString(sujeto.Kind)}&subjectId={Uri.EscapeDataString(sujeto.Id)}", ct);

    /// <summary>Arranca la instancia; con la misma llave devuelve la que ya arrancó.</summary>
    public Task<Result<InstanciaDeLlegadaDto>> ArrancarAsync(string proceso, Ref sujeto, IdempotencyKey key, CancellationToken ct)
        => Post<InstanciaDeLlegadaDto>(Workflow, "v1/instances",
            new { definitionKey = proceso, subjectKind = sujeto.Kind, subjectId = sujeto.Id }, key, ct);

    public Task<Result<InstanciaDeLlegadaDto>> DispararAsync(
        string instancia, string transicion, Ref actor, IReadOnlyList<string> roles, IdempotencyKey key, CancellationToken ct)
        => Post<InstanciaDeLlegadaDto>(Workflow, $"v1/instances/{Uri.EscapeDataString(instancia)}/fire",
            new { transition = transicion, actorKind = actor.Kind, actorId = actor.Id, actorRoles = roles }, key, ct);

    /// <summary>Sella un contenido con la llave vigente de su propósito (<c>/v1/seals</c>): determinista y no vence.</summary>
    public Task<Result<SelloDto>> SellarAsync(string llave, string contenido, CancellationToken ct)
        => Post<SelloDto>(Signing, "v1/seals", new { purpose = llave, payload = contenido }, null, ct);

    public Task<Result<SelloComprobadoDto>> ComprobarSelloAsync(string llave, string contenido, string sello, CancellationToken ct)
        => Post<SelloComprobadoDto>(Signing, "v1/seals/verify", new { purpose = llave, payload = contenido, seal = sello }, null, ct);
}
