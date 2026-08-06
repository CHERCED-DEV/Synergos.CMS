using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// El flujo de reserva de hotel llevado por el motor en proceso.
/// </summary>
/// <remarks>
/// <para><b>Esto vivía dentro de <c>BookingController</c></b> y se movió sin cambiarle una regla
/// (HU #36). Lo que se gana no es estética: acá se puede probar el orden en que se abre la caja
/// sin levantar ASP.NET, y se puede cambiar por dónde se reserva sin reescribir el borde.</para>
///
/// <para><b>Lleva dentro dos defectos ya corregidos</b>, y por eso los comentarios se conservan
/// tal cual: el apartado vencido que se cobraba igual, y la cancelación repetida que devolvía dos
/// veces. Los dos son de ORDEN, que es justo lo que un orquestador existe para llevar — y por eso
/// el destino natural de esta costura es <c>Bff.Viajes</c>.</para>
///
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de Umbraco/AspNetCore
/// (ADR 0002). El formateo de precios y el código de estado HTTP se quedaron en el borde, que es
/// donde son de verdad.</para>
/// </remarks>
public sealed class StubHotelBookingService : IHotelBookingService
{
    private readonly IReservationService _reservations;
    private readonly IPaymentProvider _payments;
    private readonly ICancellationPolicyEvaluator _cancellationPolicy;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;

    public StubHotelBookingService(
        IReservationService reservations,
        IPaymentProvider payments,
        ICancellationPolicyEvaluator cancellationPolicy,
        IAuditTrailWriter? audit = null,
        Func<DateTimeOffset>? now = null)
    {
        _reservations = reservations ?? throw new ArgumentNullException(nameof(reservations));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _cancellationPolicy = cancellationPolicy ?? throw new ArgumentNullException(nameof(cancellationPolicy));
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<Reservation> HoldAsync(ReservationRequest request, CancellationToken cancellationToken = default)
        => _reservations.HoldAsync(request, cancellationToken);

    public Task<Reservation?> GetAsync(string reservationId, CancellationToken cancellationToken = default)
        => _reservations.GetAsync(reservationId, cancellationToken);

    public async Task<HotelPaymentResult?> PayAsync(string reservationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reservationId)) return null;

        var reservation = await _reservations.GetAsync(reservationId, cancellationToken);
        if (reservation is null) return null;

        // Idempotencia: si ya está confirmada, devolver el estado actual sin volver a cobrar.
        if (reservation.Status == ReservationStatus.Confirmed)
        {
            return new HotelPaymentResult(reservation, PaymentStatus.Captured,
                reservation.PaymentSessionId, reservation.TotalPrice, null,
                HotelPaymentOutcome.AlreadyConfirmed);
        }

        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return new HotelPaymentResult(reservation, PaymentStatus.Cancelled, null, 0m,
                "La reserva está cancelada; no se puede cobrar.", HotelPaymentOutcome.Conflict);
        }

        // El hold vencido se corta ANTES de abrir la sesión de pago.
        //
        // Las dos guardas de arriba cubren Confirmed y Cancelled, y un hold vencido no es
        // ninguna de las dos: caía derecho a CreateSessionAsync + CaptureAsync —dinero
        // capturado— y solo entonces ConfirmAsync lanzaba porque el hold ya no valía. Nadie
        // atrapaba esa excepción: HTTP 500, el huésped cobrado, sin reserva, y sin Void ni
        // Refund que compensara. También se alcanza cuando el escáner de vencimientos voltea
        // el hold entre el hold y el pago, que es una carrera de minutos, no de milisegundos.
        //
        // Cobrar y después descubrir que no se puede confirmar es el orden equivocado: el
        // cupo se verifica primero, la caja se abre después.
        if (reservation.Status == ReservationStatus.Expired
            || (reservation.Status == ReservationStatus.Held && reservation.ExpiresAt <= _now()))
        {
            // No se registra acá: Application no tiene sink de logs y no vale la pena inventarle
            // uno (CLAUDE.md §6). El borde sabe que pasó —le llega Conflict con la reserva en
            // Expired— y lo grita desde donde sí hay logger.
            return new HotelPaymentResult(
                reservation with { Status = ReservationStatus.Expired },
                PaymentStatus.Cancelled, null, 0m,
                "El hold de la reserva venció; vuelve a apartar el cupo antes de pagar.",
                HotelPaymentOutcome.Conflict);
        }

        var session = await _payments.CreateSessionAsync(
            new PaymentSessionRequest(
                OrderReference: reservation.Id,
                Amount: reservation.TotalPrice,
                Currency: reservation.Currency,
                Items: new[]
                {
                    new PaymentLineItem(
                        Sku: $"{reservation.RoomTypeCode}/{reservation.RatePlanCode}",
                        Description: $"Reserva {reservation.RoomTypeCode} ({reservation.CheckIn:yyyy-MM-dd} → {reservation.CheckOut:yyyy-MM-dd})",
                        UnitPrice: reservation.TotalPrice,
                        Quantity: 1),
                },
                CustomerEmail: reservation.GuestEmail,
                ReturnUrl: null,
                Metadata: null),
            cancellationToken);

        var capture = await _payments.CaptureAsync(session.SessionId, cancellationToken: cancellationToken);

        if (capture.Status != PaymentStatus.Captured)
        {
            // El cobro no se completó (RequiresAction / Failed / etc.). No se confirma la
            // reserva; el cliente reintenta o sigue la acción que le pida su riel: redirect en
            // PSE, reto embebido en 3DS, o aprobación en el celular con Nequi (ADR 0116).
            var failureReason = capture.FailureReason
                ?? (session.Action is { Kind: not PaymentActionKind.None }
                    ? "El pago requiere una acción adicional del cliente."
                    : null);

            return new HotelPaymentResult(reservation, capture.Status, session.SessionId,
                capture.AmountCaptured, failureReason, HotelPaymentOutcome.NotCaptured);
        }

        var confirmed = await _reservations.ConfirmAsync(reservation.Id, session.SessionId, cancellationToken);

        return new HotelPaymentResult(confirmed, capture.Status,
            confirmed.PaymentSessionId ?? session.SessionId, capture.AmountCaptured, null,
            HotelPaymentOutcome.Confirmed);
    }

    public async Task<HotelCancellationResult?> CancelAsync(
        string reservationId, string? reason, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reservationId)) return null;

        var reservation = await _reservations.GetAsync(reservationId, cancellationToken);
        if (reservation is null) return null;

        var today = DateOnly.FromDateTime(_now().UtcDateTime);

        // Cancelar dos veces NO reembolsa dos veces.
        //
        // La guarda de abajo mira la política y la sesión de pago, nunca el ESTADO. Y
        // CancelAsync es idempotente pero no limpia el PaymentSessionId, así que una segunda
        // llamada con el mismo id volvía a evaluar la política y a llamar a RefundAsync por
        // el mismo monto. Hoy no se duplica la plata solo porque el proveedor stub reembolsa
        // únicamente sesiones capturadas y en la segunda pasada encuentra Refunded: el PSP
        // salva al llamador, el llamador no se salva solo. Un gateway real que acepte
        // reembolsos parciales sucesivos —el caso normal cuando hay penalidad, porque quedan
        // pesos sin reembolsar en la sesión— paga dos veces.
        //
        // RefundAsync es además el ÚNICO método mutador de IPaymentProvider cuyo contrato no
        // promete idempotencia; CaptureAsync y VoidAsync sí la prometen explícitamente. Así
        // que la garantía tiene que ponerla quien llama.
        if (reservation.Status == ReservationStatus.Cancelled)
        {
            var settled = _cancellationPolicy.Evaluate(reservation.RatePlanCode, reservation.CheckIn, today);
            // RefundStatus nulo y no "Refunded": esta pasada no movió dinero, y afirmar un
            // reembolso que no ocurrió aquí es la clase de dato con cara de verdad que ya costó
            // una vez en este mismo flujo.
            return new HotelCancellationResult(
                reservation, settled.Refundable, settled.PenaltyAmount, settled.Description, null);
        }

        var motivo = string.IsNullOrWhiteSpace(reason) ? "guest-requested" : reason!.Trim();
        var outcome = _cancellationPolicy.Evaluate(reservation.RatePlanCode, reservation.CheckIn, today);

        var cancelled = await _reservations.CancelAsync(reservation.Id, motivo, cancellationToken);

        // DEVOLVER LA PLATA, no sólo calcular cuánta. Antes esto evaluaba la política,
        // informaba el monto reembolsable, cancelaba la reserva y NUNCA llamaba al motor de
        // pago: la cifra era decorativa y el huésped se quedaba sin su dinero y con un mensaje
        // diciéndole que se lo devolvíamos.
        //
        // Lo que se devuelve es el total MENOS la penalidad. Si no hay sesión de pago —una
        // reserva que nunca se cobró— no hay nada que devolver.
        string? refundState = null;
        if (outcome.Refundable && !string.IsNullOrWhiteSpace(reservation.PaymentSessionId))
        {
            var refundable = reservation.TotalPrice - outcome.PenaltyAmount;
            if (refundable > 0m)
            {
                var refund = await _payments.RefundAsync(
                    reservation.PaymentSessionId!, refundable, cancellationToken);
                refundState = refund.Status.ToString();

                // La reserva YA quedó cancelada y eso no se deshace: el cupo volvió al
                // inventario. Si el reembolso falló, `RefundStatus` sale con lo que dijo el
                // proveedor y NO en nulo — callarlo reproduciría el defecto que este flujo
                // cierra. Quien tiene logger lo grita; acá se devuelve el hecho.
            }
        }

        // Rastro de la cancelación (ADR 0037), best-effort y después de que ya ocurrió: si el
        // registro falla, desandar una cancelación ya sellada sería peor.
        //
        // El endpoint que llega acá es ANÓNIMO —el reservationId es la credencial, decisión
        // deliberada para que quien compró como invitado vuelva a su reserva— y a la vez
        // DESTRUCTIVO y con movimiento de plata. Sin rastro no había forma de responder «¿quién
        // canceló esta estadía y cuándo?».
        //
        // El actor es el huésped de la reserva, NO quien hizo la petición: no hay sesión que
        // consultar. Es una limitación honesta del modelo de credencial-por-URL, y queda dicha
        // aquí para que nadie lea este registro como una identificación del solicitante.
        if (_audit is not null)
        {
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                new AuditEvent(
                    Id: Guid.NewGuid().ToString("N"),
                    OccurredAtUtc: _now().UtcDateTime,
                    ActorEmail: cancelled.GuestEmail,
                    ActorName: cancelled.GuestName,
                    Action: "booking.reservation.cancelled",
                    Resource: cancelled.Id,
                    Outcome: "success",
                    Detail: $"Cancelada por URL-credencial; motivo '{motivo}'; penalidad " +
                        $"{outcome.PenaltyAmount} {cancelled.Currency}; reembolso {refundState ?? "no aplica"}."),
                cancellationToken), cancellationToken);
        }

        return new HotelCancellationResult(
            cancelled, outcome.Refundable, outcome.PenaltyAmount, outcome.Description, refundState);
    }
}
