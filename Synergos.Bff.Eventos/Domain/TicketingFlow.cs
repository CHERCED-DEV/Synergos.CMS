using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Clients;
using Synergos.Core;
using Compensation = Synergos.Bff.Core.Compensation;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>Una línea de la compra: qué localidad, qué butaca si la hay, y cuántas.</summary>
/// <remarks>
/// <b>El precio NO está acá</b>, y es deliberado. Si viajara en la petición, cualquiera compraría
/// la localidad VIP al precio de la general cambiando un número. Se cotiza contra
/// <c>Api.Pricing</c> y lo que el llamador mandó no se mira.
/// </remarks>
public sealed record TicketLine(string Tier, string? Seat, int Quantity);

/// <summary>
/// El flujo de comprar entradas — <b>el ORDEN, que es lo del dominio</b>.
/// </summary>
/// <remarks>
/// <para><b>Tres capacidades y ninguna sabe que existe un evento.</b> Pricing sabe de precios,
/// Inventory de pozos contables y Payments de plata. Que el aforo se aparta ANTES de cobrar, que
/// la butaca se consume DESPUÉS de capturar, y que si algo falla a la mitad hay que devolver
/// las dos cosas — eso no lo sabe ninguna, y es lo que vive acá.</para>
///
/// <para><b>Las dos fases, otra vez.</b> Comprar aparta y <i>autoriza</i>; confirmar
/// <i>captura</i> y consume. El caso más común de fallo —el comprador se arrepiente, la tarjeta
/// rechaza, el apartado se vence— no cuesta una devolución. Que el tercer dominio llegue a la
/// misma forma que los dos anteriores es lo que hace compartible la máquina de sagas.</para>
///
/// <para><b>Lo que este dominio NO comparte con la tienda:</b> no hay pedido ni despacho. Una
/// entrada no se envía. El artefacto —el e-ticket con su QR— lo emite el CMS después de que esto
/// conteste que sí, porque el firmante vive allá y un QR no es cupo ni es plata.</para>
///
/// <para><b>Y el orden dentro de confirmar</b> sigue la regla que enseñó una corrida real en
/// Tienda: <i>lo que cierra una puerta va lo más tarde posible</i>. Acá se captura primero
/// —el fallo que deja plata cobrada sin butaca se deshace solo; el que deja butaca entregada sin
/// cobrar exige perseguir a una persona— y se consume después.</para>
/// </remarks>
public sealed class TicketingFlow
{
    /// <summary>Cuántas líneas admite una compra de entradas.</summary>
    /// <remarks>
    /// Sin tope, una petición con mil líneas dispara mil apartados y mil compensaciones — y el
    /// barrido de una sola saga rendida martillearía <c>Api.Inventory</c> mil veces por vuelta.
    /// El número es más bajo que el de la tienda a propósito: nadie compra cincuenta butacas
    /// sueltas en una transacción, y quien lo intente es más probable que sea un bot.
    /// </remarks>
    public const int MaxLines = 20;

    /// <summary>Cuántas entradas admite una línea de cupo general.</summary>
    public const int MaxPorLinea = 10;

    private readonly EventosCapabilities _caps;
    private readonly SagaEngine<TicketingSaga> _sagas;
    private readonly TimeProvider _clock;
    private readonly ILogger<TicketingFlow> _log;

    public TicketingFlow(
        EventosCapabilities caps, SagaEngine<TicketingSaga> sagas,
        TimeProvider clock, ILogger<TicketingFlow> log)
    {
        _caps = caps;
        _sagas = sagas;
        _clock = clock;
        _log = log;
    }

    /// <summary>Aparta el aforo y autoriza el cobro. No mueve plata todavía.</summary>
    public async Task<Result<TicketingSaga>> BuyAsync(
        string eventId, Ref buyer, IReadOnlyList<TicketLine> lineas, string sagaId, CancellationToken ct)
    {
        // La saga existe ANTES de tocar nada. Si el proceso se cae después del primer paso, lo
        // que se hizo queda escrito con su identificador — y como las llaves derivan de él,
        // repetir la llamada con el mismo sagaId no duplica nada.
        var previa = _sagas.Find(sagaId);
        if (previa is not null) return Result.Ok(previa);

        var motivo = Revisar(lineas);
        if (motivo is not null) return Result.Rejected<TicketingSaga>(motivo);

        // 1. Cuánto cuesta, contra la capacidad. Va antes de apartar porque un precio que no se
        //    puede cotizar aborta la compra sin haber tocado el aforo de nadie.
        //
        //    Se cotiza por LOCALIDAD, sin la butaca: dos butacas de la misma localidad valen lo
        //    mismo, y meter el asiento obligaría a cargar un precio por butaca.
        var aCotizar = lineas
            .GroupBy(l => l.Tier, StringComparer.Ordinal)
            .Select(g => (Subject: AforoSubject.PriceOf(eventId, g.Key), Quantity: g.Sum(l => l.Quantity)))
            .ToList();

        var quote = await _caps.QuoteAsync(aCotizar, ct);
        if (!quote.IsOk) return Result.Rejected<TicketingSaga>(quote.Rejection!);
        var total = Money.Of(quote.Value.Total.Amount, quote.Value.Total.Currency);

        var saga = new TicketingSaga(sagaId, buyer, eventId, SagaStatus.Running,
            Array.Empty<SeatHold>(), null, total,
            Array.Empty<Compensation>(), null, _clock.GetUtcNow());

        // 2. Apartar aforo, UNA LÍNEA A LA VEZ. Cada apartado se anota como compensable en el
        //    mismo momento en que existe: si el proceso se cae en la línea cuatro, las tres
        //    primeras ya tienen quién las suelte (feedback_compensation_is_data).
        foreach (var linea in lineas)
        {
            var subject = AforoSubject.For(eventId, linea.Tier, linea.Seat);

            var item = await _caps.FindAforoAsync(subject, ct);
            if (!item.IsOk) return await AbortarAsync(saga, item.Rejection!, "esa localidad no tiene aforo declarado", ct);

            // La llave lleva el ítem y no el índice de la línea: si el comprador reordena las
            // butacas entre dos intentos, una llave por posición apartaría dos veces.
            var hold = await _caps.HoldAforoAsync(item.Value.Id, linea.Quantity,
                Ref.Create("eventos.compra", sagaId), saga.KeyFor($"hold:{item.Value.Id}"), ct);
            if (!hold.IsOk) return await AbortarAsync(saga, hold.Rejection!, "no se pudo apartar el aforo", ct);

            saga = saga with
            {
                Holds = saga.Holds
                    .Append(new SeatHold(hold.Value.Id, item.Value.Id, linea.Quantity, linea.Tier, linea.Seat))
                    .ToList(),
                Compensations = saga.Compensations
                    .Append(Compensation.For(EventosCompensations.ReleaseSeatHold, hold.Value.Id, "compra no confirmada"))
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 3. Autorizar: reserva cupo en el medio de pago SIN mover plata.
        var pago = await _caps.AuthorizeAsync(Ref.Create("eventos.compra", sagaId), buyer, total,
            saga.KeyFor("authorize"), ct);
        if (!pago.IsOk) return await AbortarAsync(saga, pago.Rejection!, "el cobro no se pudo autorizar", ct);

        saga = saga with
        {
            PaymentId = pago.Value.Id,
            Compensations = saga.Compensations
                .Append(Compensation.For(EventosCompensations.VoidPayment, pago.Value.Id, "compra no confirmada"))
                .ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>Captura el cobro y consume el aforo. A partir de acá hay plata movida.</summary>
    public async Task<Result<TicketingSaga>> ConfirmAsync(string sagaId, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null)
        {
            return Rejection.NotFound("eventos.purchase_not_found", $"No existe la compra {sagaId}.");
        }
        if (saga.Status == SagaStatus.Completed) return Result.Ok(saga);   // idempotente
        if (saga.Status != SagaStatus.Running)
        {
            return Rejection.Conflict("eventos.not_confirmable", $"La compra está {saga.Status}.");
        }

        // 1. Capturar. A partir de acá hay plata movida, y todo fallo cuesta una devolución.
        if (saga.PaymentId is { } paymentId)
        {
            var capturado = await _caps.CaptureAsync(paymentId, saga.KeyFor("capture"), ct);
            if (!capturado.IsOk) return await AbortarAsync(saga, capturado.Rejection!, "el cobro no se pudo capturar", ct);

            // La compensación del pago CAMBIA DE CARÁCTER: de «liberar autorización» a
            // «devolver». Liberar una autorización ya capturada Api.Payments lo rechaza, así que
            // sin este cambio la compensación fallaría siempre y quedaría colgada para siempre
            // por una razón que no tiene nada que ver con el mundo real
            // (feedback_compensation_changes_character).
            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == EventosCompensations.VoidPayment && c.IsPending
                        ? c with { Kind = EventosCompensations.RefundPayment }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 2. Consumir el aforo, UNO POR APARTADO. Cada consumo reescribe su propia compensación
        //    en el acto: soltar un apartado ya consumido lo rechaza Api.Inventory —«devolver
        //    existencias es un ajuste, no una liberación»—, así que a partir de acá deshacer
        //    significa devolver unidades al pozo.
        //
        //    Reescribirla DENTRO del bucle y no al final importa: si el consumo falla en la
        //    tercera butaca, las dos primeras ya están consumidas y su compensación tiene que ser
        //    la buena.
        foreach (var hold in saga.Holds)
        {
            var consumido = await _caps.ConsumeAforoAsync(hold.HoldId, ct);
            if (!consumido.IsOk) return await AbortarAsync(saga, consumido.Rejection!, "no se pudo consumir el aforo apartado", ct);

            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == EventosCompensations.ReleaseSeatHold && c.TargetId == hold.HoldId && c.IsPending
                        ? c with { Kind = EventosCompensations.RestockSeats, TargetId = hold.ItemId }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // Salió: ya no hay nada que deshacer. Las compensaciones se marcan como hechas para que
        // el barrido no las intente.
        //
        // NO se emite el e-ticket acá, y no es un olvido: el QR lo firma el CMS, que es donde
        // vive el firmante. Un orquestador que emitiera artefactos tendría estado propio más allá
        // de sus sagas, y entonces sería una capacidad mal cortada.
        var ahora = _clock.GetUtcNow();
        saga = saga with
        {
            Status = SagaStatus.Completed,
            LastError = null,
            Compensations = saga.Compensations.Select(c => c.IsPending ? c with { DoneAtUtc = ahora } : c).ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>Cancela una compra todavía sin confirmar.</summary>
    public Task<Result<TicketingSaga>> CancelAsync(string sagaId, CancellationToken ct)
        => _sagas.CompensateAsync(sagaId, "cancelada por el comprador", ct);

    /// <summary>Vuelve a intentar lo que se había rendido.</summary>
    public Task<Result<TicketingSaga>> RetryStuckAsync(string sagaId, CancellationToken ct)
        => _sagas.RetryStuckAsync(sagaId, ct);

    public Result<TicketingSaga> Get(string id)
        => _sagas.Find(id) is { } s
            ? Result.Ok(s)
            : Rejection.NotFound("eventos.purchase_not_found", $"No existe la compra {id}.");

    public IReadOnlyList<TicketingSaga> PendingCompensations() => _sagas.PendingCompensations();

    /// <summary>Lo que una compra de entradas tiene que cumplir.</summary>
    private static Rejection? Revisar(IReadOnlyList<TicketLine> lineas)
    {
        if (lineas.Count == 0)
        {
            return Rejection.Invalid("eventos.no_lines", "No se puede comprar sin entradas.");
        }
        if (lineas.Count > MaxLines)
        {
            return Rejection.Invalid("eventos.too_many_lines",
                $"Una compra admite hasta {MaxLines} líneas y esa trae {lineas.Count}.");
        }
        if (lineas.Any(l => string.IsNullOrWhiteSpace(l.Tier)))
        {
            return Rejection.Invalid("eventos.bad_tier", "Cada línea necesita su localidad.");
        }
        if (lineas.Any(l => l.Quantity <= 0 || l.Quantity > MaxPorLinea))
        {
            return Rejection.Invalid("eventos.bad_quantity",
                $"Cada línea va de 1 a {MaxPorLinea} entradas.");
        }

        // Una butaca nominada es UNA. Pedir tres veces la misma butaca no es una compra de tres:
        // es un error que, sin esta guarda, apartaría tres unidades de un pozo que tiene una y
        // se rechazaría con un motivo que no explica nada.
        if (lineas.Any(l => !string.IsNullOrWhiteSpace(l.Seat) && l.Quantity != 1))
        {
            return Rejection.Invalid("eventos.seat_is_one",
                "Una butaca nominada es una sola entrada.");
        }

        // Y la misma butaca no se puede pedir dos veces en la misma compra. Sin esto, las dos
        // líneas resolverían el mismo pozo y la MISMA llave de idempotencia: el segundo apartado
        // devolvería el primero, la compra parecería tener dos butacas y solo habría una.
        var butacas = lineas
            .Where(l => !string.IsNullOrWhiteSpace(l.Seat))
            .Select(l => $"{l.Tier}/{l.Seat}")
            .ToList();
        if (butacas.Count != butacas.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            return Rejection.Invalid("eventos.duplicate_seat",
                "La misma butaca aparece dos veces en la compra.");
        }

        return null;
    }

    /// <summary>
    /// Anota el fallo, deshace lo que ya se hizo, y devuelve el rechazo original.
    /// </summary>
    /// <remarks>
    /// <b>Se devuelve el rechazo de la capacidad y no uno propio</b>: quien llamó necesita saber
    /// si fue <c>inventory.out_of_stock</c> —ofrecer otra localidad— o <c>payments.declined</c>
    /// —pedir otro medio de pago—, y aplanarlos a «no se pudo comprar» deja al comprador sin nada
    /// que hacer.
    /// </remarks>
    private async Task<Result<TicketingSaga>> AbortarAsync(
        TicketingSaga saga, Rejection motivo, string razon, CancellationToken ct)
    {
        _log.LogWarning("La compra {Saga} se deshace ({Razon}): {Error}", saga.Id, razon, motivo);
        _sagas.Put(saga with { LastError = motivo.ToString() });
        await _sagas.CompensateAsync(saga.Id, razon, ct);
        return Result.Rejected<TicketingSaga>(motivo);
    }
}
