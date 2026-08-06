using Synergos.Api.Payments.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Payments.Domain;

/// <summary>Compone las reglas de <see cref="PaymentRules"/> con el almacén y el proveedor.</summary>
public sealed class PaymentService
{
    private readonly IPaymentStore _payments;
    private readonly IPaymentProvider _provider;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    public PaymentService(IPaymentStore payments, IPaymentProvider provider, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _payments = payments;
        _provider = provider;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    public Result<Payment> Authorize(Ref forWhat, Ref payer, Money amount, IdempotencyKey key)
    {
        lock (_gate)
        {
            // De todas las llaves de idempotencia del sistema, esta es la que más duele si
            // falla: un reintento tras un timeout cobra dos veces a una persona real.
            if (_idempotency.Find("payment", key) is { } yaEra)
            {
                return _payments.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{PaymentRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el cobro no está.");
            }

            var motivo = PaymentRules.CheckAmount(amount);
            if (motivo is not null) return Result.Rejected<Payment>(motivo);

            var intento = _provider.Authorize(amount, payer);
            var referencia = intento.IsOk ? intento.Reference : null;
            var id = Guid.NewGuid().ToString("n");

            // El intento se registra HAYA SIDO ACEPTADO O NO. Un rechazo sin rastro deja al
            // cliente diciendo "yo lo intenté" y al sistema sin manera de saber si es cierto.
            var payment = new Payment(
                id, forWhat, payer, amount,
                referencia is null ? PaymentStatus.Failed : PaymentStatus.Authorized,
                _provider.Name, referencia, Array.Empty<Refund>(), Now);

            _payments.Put(payment);
            _idempotency.Remember("payment", key, id);

            // El motivo sale tal cual del proveedor, y el reintento depende de CUÁL fue: un
            // rechazo firme no se reintenta y una caída sí.
            var rechazo = PaymentRules.FromAttempt(intento, "la autorización");
            return rechazo is not null ? Result.Rejected<Payment>(rechazo) : Result.Ok(payment);
        }
    }

    public Result<Payment> Get(string id)
        => _payments.Find(id) is { } p
            ? Result.Ok(p)
            : Rejection.NotFound($"{PaymentRules.CodePrefix}.payment_not_found", $"No existe el cobro {id}.");

    public Result<Payment> Capture(string id, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("capture", key) is { } yaEra)
            {
                return _payments.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{PaymentRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el cobro no está.");
            }

            var payment = _payments.Find(id);
            if (payment is null)
            {
                return Rejection.NotFound($"{PaymentRules.CodePrefix}.payment_not_found", $"No existe el cobro {id}.");
            }

            var motivo = PaymentRules.CheckCapturable(payment);
            if (motivo is not null) return Result.Rejected<Payment>(motivo);

            var capturaIntento = _provider.Capture(payment.ProviderReference!, payment.Amount);
            if (PaymentRules.FromAttempt(capturaIntento, "la captura") is { } falloCaptura)
            {
                // La autorización sigue en pie pase lo que pase: no se toca el estado.
                return Result.Rejected<Payment>(falloCaptura);
            }

            var capturado = payment with { Status = PaymentStatus.Captured, CapturedAtUtc = Now };
            _payments.Put(capturado);
            _idempotency.Remember("capture", key, id);
            return Result.Ok(capturado);
        }
    }

    public Result<Payment> Void(string id)
    {
        lock (_gate)
        {
            var payment = _payments.Find(id);
            if (payment is null)
            {
                return Rejection.NotFound($"{PaymentRules.CodePrefix}.payment_not_found", $"No existe el cobro {id}.");
            }

            var motivo = PaymentRules.CheckVoidable(payment);
            if (motivo is not null) return Result.Rejected<Payment>(motivo);
            if (payment.Status == PaymentStatus.Voided) return Result.Ok(payment);

            if (PaymentRules.FromAttempt(_provider.Void(payment.ProviderReference!), "la liberación") is { } falloVoid)
            {
                return Result.Rejected<Payment>(falloVoid);
            }

            var liberado = payment with { Status = PaymentStatus.Voided };
            _payments.Put(liberado);
            return Result.Ok(liberado);
        }
    }

    /// <summary>
    /// Devuelve, total o parcialmente.
    /// </summary>
    /// <remarks>
    /// <b>Este es el camino de la compensación.</b> Cuando un flujo que cruza capacidades falla
    /// después de cobrar —se cobró el evento y el cupo se fue—, el BFF llama acá. Por eso una
    /// devolución tiene <c>Reason</c>: sin él, una devolución compensatoria y una pedida por el
    /// cliente son indistinguibles en la conciliación.
    /// </remarks>
    public Result<Payment> RefundPayment(string id, Money amount, string? reason, IdempotencyKey key)
    {
        lock (_gate)
        {
            if (_idempotency.Find("refund", key) is { } yaEra)
            {
                return _payments.Find(yaEra) is { } previo
                    ? Result.Ok(previo)
                    : Rejection.Conflict($"{PaymentRules.CodePrefix}.idempotency_orphan", "La llave ya se usó pero el cobro no está.");
            }

            var payment = _payments.Find(id);
            if (payment is null)
            {
                return Rejection.NotFound($"{PaymentRules.CodePrefix}.payment_not_found", $"No existe el cobro {id}.");
            }

            var motivo = PaymentRules.CheckRefundable(payment, amount);
            if (motivo is not null) return Result.Rejected<Payment>(motivo);

            if (PaymentRules.FromAttempt(_provider.Refund(payment.ProviderReference!, amount), "la devolución") is { } falloRefund)
            {
                return Result.Rejected<Payment>(falloRefund);
            }

            var devoluciones = payment.Refunds.Append(
                new Refund(Guid.NewGuid().ToString("n"), amount, reason, Now)).ToList();

            var actualizado = payment with { Refunds = devoluciones };
            _payments.Put(actualizado);
            _idempotency.Remember("refund", key, id);
            return Result.Ok(actualizado);
        }
    }

    public Result<Page<Payment>> ListFor(Ref? subject, int offset, int limit)
    {
        if (subject is null)
        {
            return Rejection.Invalid($"{PaymentRules.CodePrefix}.subject_required",
                "Hace falta filtrar por aquello que se cobra: sin filtro esto es un volcado del almacén.");
        }

        var todos = _payments.ForSubject(subject)
            .OrderByDescending(p => p.AuthorizedAtUtc)
            .ThenBy(p => p.Id, StringComparer.Ordinal)
            .ToList();

        return Result.Ok(new Page<Payment>(todos.Skip(offset).Take(limit).ToList(), todos.Count, offset));
    }
}
