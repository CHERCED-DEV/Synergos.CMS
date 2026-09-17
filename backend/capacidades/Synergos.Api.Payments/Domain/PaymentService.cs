using Synergos.Api.Payments.Storage;
using Synergos.Core;
using Synergos.Shared;

namespace Synergos.Api.Payments.Domain;

/// <summary>Compone las reglas de <see cref="PaymentRules"/> con el almacén y el proveedor.</summary>
public sealed class PaymentService : IDisposable
{
    private readonly IPaymentStore _payments;
    private readonly IPaymentProvider _provider;
    private readonly IIdempotencyLedger _idempotency;
    private readonly TimeProvider _clock;

    /// <summary>
    /// El cerrojo que hace que comprobar y escribir sean una sola operación.
    /// </summary>
    /// <remarks>
    /// <b>Es un <see cref="SemaphoreSlim"/> y no un <c>lock</c> porque dentro se espera a la
    /// pasarela</b> (HU #27): <c>await</c> no cabe en un <c>lock</c>, y sacar la llamada fuera
    /// del cerrojo rompería justo lo que <c>CheckRefundable</c> necesita — dos devoluciones
    /// parciales simultáneas que, sumadas, devuelven más de lo que entró. Con el semáforo el
    /// hilo se suelta mientras la pasarela contesta, que es lo contrario de lo que hacía el
    /// <c>lock</c>.
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <inheritdoc />
    public void Dispose() => _gate.Dispose();

    public PaymentService(IPaymentStore payments, IPaymentProvider provider, IIdempotencyLedger idempotency, TimeProvider clock)
    {
        _payments = payments;
        _provider = provider;
        _idempotency = idempotency;
        _clock = clock;
    }

    private DateTimeOffset Now => _clock.GetUtcNow();

    /// <summary>Autoriza un cobro.</summary>
    /// <param name="assertion">
    /// Con qué se afirmó la identidad de quien paga, <b>ya resuelto por el borde</b>.
    /// <para>Va SIN valor por defecto a propósito: un default aquí lo escribiría un llamador que
    /// no lo pensó, y «CmsSession» no es una respuesta neutra —significa «nos fiamos de quien
    /// llama»—. Una clave omitida que el otro lado resuelve con un default deja de ser un hueco y
    /// pasa a AFIRMAR (<c>feedback_an_omitted_key_can_be_an_assertion</c>), y sobre quién movió
    /// plata eso es exactamente lo que no puede pasar.</para>
    /// </param>
    public async Task<Result<Payment>> AuthorizeAsync(
        Ref forWhat, Ref payer, Money amount, IdentityAssertion assertion,
        IdempotencyKey key, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
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

            var intento = await _provider.AuthorizeAsync(amount, payer, ct).ConfigureAwait(false);
            var referencia = intento.IsOk ? intento.Reference : null;
            var id = Guid.NewGuid().ToString("n");

            // El intento se registra HAYA SIDO ACEPTADO O NO. Un rechazo sin rastro deja al
            // cliente diciendo "yo lo intenté" y al sistema sin manera de saber si es cierto.
            var payment = new Payment(
                id, forWhat, payer, amount,
                referencia is null ? PaymentStatus.Failed : PaymentStatus.Authorized,
                _provider.Name, referencia, Array.Empty<Refund>(), Now,
                CapturedAtUtc: null, ActionUrl: intento.ActionUrl,
                // Se guarda TAMBIÉN cuando el cobro falla. Un intento rechazado sin saber quién
                // lo hizo es la mitad de un rastro: el registro existe justamente para poder
                // contestar «quién intentó pagar esto», y eso no depende de que saliera bien.
                PaidWith: assertion);

            _payments.Put(payment);
            _idempotency.Remember("payment", key, id);

            // El motivo sale tal cual del proveedor, y el reintento depende de CUÁL fue: un
            // rechazo firme no se reintenta y una caída sí.
            var rechazo = PaymentRules.FromAttempt(intento, "la autorización");
            return rechazo is not null ? Result.Rejected<Payment>(rechazo) : Result.Ok(payment);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Result<Payment> Get(string id)
        => _payments.Find(id) is { } p
            ? Result.Ok(p)
            : Rejection.NotFound($"{PaymentRules.CodePrefix}.payment_not_found", $"No existe el cobro {id}.");

    public async Task<Result<Payment>> CaptureAsync(string id, IdempotencyKey key, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
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

            var capturaIntento = await _provider
                .CaptureAsync(payment.ProviderReference!, payment.Amount, ct).ConfigureAwait(false);
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
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Result<Payment>> VoidAsync(string id, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var payment = _payments.Find(id);
            if (payment is null)
            {
                return Rejection.NotFound($"{PaymentRules.CodePrefix}.payment_not_found", $"No existe el cobro {id}.");
            }

            var motivo = PaymentRules.CheckVoidable(payment);
            if (motivo is not null) return Result.Rejected<Payment>(motivo);
            if (payment.Status == PaymentStatus.Voided) return Result.Ok(payment);

            var intentoVoid = await _provider
                .VoidAsync(payment.ProviderReference!, ct).ConfigureAwait(false);
            if (PaymentRules.FromAttempt(intentoVoid, "la liberación") is { } falloVoid)
            {
                return Result.Rejected<Payment>(falloVoid);
            }

            var liberado = payment with { Status = PaymentStatus.Voided };
            _payments.Put(liberado);
            return Result.Ok(liberado);
        }
        finally
        {
            _gate.Release();
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
    public async Task<Result<Payment>> RefundPaymentAsync(
        string id, Money amount, string? reason, IdempotencyKey key, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
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

            var intentoRefund = await _provider
                .RefundAsync(payment.ProviderReference!, amount, ct).ConfigureAwait(false);
            if (PaymentRules.FromAttempt(intentoRefund, "la devolución") is { } falloRefund)
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
        finally
        {
            _gate.Release();
        }
    }


    /// <summary>
    /// Anota lo que el proveedor cuenta de un cobro suyo.
    /// </summary>
    /// <remarks>
    /// <para><b>Es el camino por el que un cobro se entera de verdad de que la plata se movió.</b>
    /// Con checkout hospedado la transacción nace cuando el comprador la completa, y con PSE el
    /// desenlace llega minutos después: preguntar en un bucle es la otra manera, y es la cara.</para>
    ///
    /// <para><b>No lleva llave de idempotencia y no la necesita</b>, que es la excepción que
    /// conviene entender. Quien llama es el proveedor, no un cliente nuestro: no hay cabecera que
    /// exigirle. Lo que hace las veces de llave es el estado —solo se avanza desde
    /// <c>Authorized</c>—, así que el mismo evento reentregado tres veces deja el cobro donde
    /// estaba y devuelve lo mismo.</para>
    /// </remarks>
    public async Task<Result<Payment>> RecordProviderEventAsync(
        string? providerReference, PaymentStatus destino, long centavosDelProveedor, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(providerReference))
        {
            return Rejection.Invalid($"{PaymentRules.CodePrefix}.webhook_unreadable",
                "El evento no dice de qué transacción habla.");
        }

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var payment = _payments.FindByProviderReference(providerReference);
            if (payment is null)
            {
                // No es de este despliegue —pasa con transacciones de otra cuenta o de un
                // despliegue anterior—, así que no es un error del proveedor y se le acusa recibo.
                return Rejection.NotFound($"{PaymentRules.CodePrefix}.unknown_provider_reference",
                    $"No hay ningún cobro con la referencia {providerReference}.");
            }

            if (PaymentRules.CheckProviderEvent(payment, destino, centavosDelProveedor) is { } motivo)
            {
                return Result.Rejected<Payment>(motivo);
            }

            if (payment.Status != PaymentStatus.Authorized) return Result.Ok(payment);

            var movido = destino == PaymentStatus.Captured
                ? payment with { Status = destino, CapturedAtUtc = Now }
                : payment with { Status = destino };

            _payments.Put(movido);
            return Result.Ok(movido);
        }
        finally
        {
            _gate.Release();
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
