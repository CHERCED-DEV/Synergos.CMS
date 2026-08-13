using Synergos.Bff.Core;
using Synergos.Bff.Viajes.Clients;
using Synergos.Core;

namespace Synergos.Bff.Viajes.Domain;

/// <summary>Un ítem pedido: qué producto, cómo se llama, y qué periodo ocupa.</summary>
/// <remarks>
/// <b>Sin precio, a propósito.</b> Si el total llegara del llamador, cualquiera reservaría la
/// suite al precio de la estándar. Se cotiza contra <c>Api.Pricing</c>.
/// </remarks>
public sealed record TripItem(string ProductRef, string ProductLabel, DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// El flujo de reservar un viaje — <b>el ORDEN, que es lo del dominio</b>.
/// </summary>
/// <remarks>
/// <para><b>Lo que quedó acá tras promover la máquina de sagas.</b> Deshacer, reintentar,
/// rendirse y avisar son iguales en los ocho dominios y viven en <see cref="SagaEngine{TSaga}"/>.
/// Lo que no se puede compartir es <i>en qué orden van los pasos</i>. Eso es el negocio.</para>
///
/// <para><b>Lo que este dominio tiene y los tres anteriores no:</b> los pasos reversibles son
/// VARIOS y HETEROGÉNEOS. Un vuelo, dos noches de hotel y un auto son cuatro apartados sobre
/// cuatro recursos con cuatro ventanas, y el fallo puede llegar en cualquiera —incluso <i>después
/// de haber confirmado los tres primeros</i>—. Es lo que convirtió a #36 de cableado en
/// orquestador.</para>
///
/// <para><b>Y de ahí sale la parte delicada: la compensación del cupo cambia de carácter.</b> En
/// Salud confirmar el cupo es el último paso y nunca hay una reserva confirmada que deshacer.
/// Acá, al confirmar el cuarto ítem los tres primeros ya son reservas — y soltar un apartado que
/// se convirtió en reserva lo rechaza <c>Api.Booking</c>. Por eso la reescritura va DENTRO del
/// bucle, ítem por ítem, en el instante en que cada uno deja de ser reversible por la vía barata.
/// Reescribirlas todas al final dejaría a las de en medio apuntando a un apartado que ya no
/// existe si el fallo llega antes de terminar.</para>
///
/// <para><b>El orden dentro de confirmar</b> es el mismo que en Salud y por la misma razón:
/// primero se captura y después se confirman los cupos. Al revés, un fallo del cobro dejaría un
/// viaje confirmado que nadie pagó, y recuperarse de eso exige llamar al viajero. Con este orden
/// el fallo deja plata cobrada sin viaje, que <b>sí</b> se deshace sola.</para>
/// </remarks>
public sealed class TripFlow
{
    /// <summary>Cuántos ítems admite un viaje.</summary>
    /// <remarks>
    /// No es una regla de negocio: es un techo para que una petición absurda no dispare cien
    /// apartados que después haya que deshacer de a uno.
    /// </remarks>
    public const int MaxItems = 12;

    private readonly ViajesCapabilities _caps;
    private readonly SagaEngine<TripSaga> _sagas;
    private readonly TimeProvider _clock;
    private readonly ILogger<TripFlow> _log;

    public TripFlow(ViajesCapabilities caps, SagaEngine<TripSaga> sagas, TimeProvider clock, ILogger<TripFlow> log)
    {
        _caps = caps;
        _sagas = sagas;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Fase 1: aparta cada ítem, cotiza el viaje entero y autoriza el cobro.
    /// </summary>
    /// <param name="partialConfirm">
    /// Qué pasa si un ítem no se puede confirmar tras cobrar. Lo decide quien vende: un paquete
    /// que no sirve partido quiere todo-o-nada; tres compras que coinciden en un carrito, no.
    /// </param>
    public async Task<Result<TripSaga>> BookAsync(
        Ref traveller, IReadOnlyList<TripItem> items, string sagaId, CancellationToken ct,
        bool partialConfirm = false)
    {
        if (Revisar(items) is { } malo) return Result.Rejected<TripSaga>(malo);

        // La saga existe ANTES de tocar nada. Si el proceso se cae después del primer apartado,
        // lo que se hizo queda escrito con su identificador — y como las llaves derivan de él,
        // repetir la llamada con el mismo sagaId no duplica nada.
        var saga = _sagas.Find(sagaId);
        if (saga is not null)
        {
            // Reintento de la misma petición: se devuelve lo que hay. La alternativa —rehacer—
            // tomaría un segundo apartado con una llave distinta.
            return Result.Ok(saga);
        }

        saga = new TripSaga(sagaId, traveller, SagaStatus.Running, Array.Empty<ItemHold>(), null,
            Money.Zero(Money.Cop), Money.Zero(Money.Cop), Array.Empty<Compensation>(), null,
            _clock.GetUtcNow(), PartialConfirm: partialConfirm);
        _sagas.Put(saga);

        // 1. Apartar, ítem por ítem. Es el paso barato y reversible: se hace antes de hablar de
        //    plata. Cada apartado se ANOTA como compensable en el mismo momento en que existe —
        //    si se anotaran todos al final, una caída en medio dejaría cupo retenido sin nada que
        //    lo suelte (`feedback_compensation_is_data`).
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            // La ventana ya se validó en Revisar(), antes de tocar ninguna capacidad: un periodo
            // invertido es un error de quien pide, y descubrirlo después de haber apartado tres
            // ítems obligaría a soltarlos por algo que se sabía desde el principio.
            var ventana = TimeWindow.Of(item.Start, item.End);

            // El recurso se resuelve DESDE el producto. Exigirle al llamador el identificador
            // interno de Api.Booking obligaría a que ese identificador viajara hasta el CMS, que
            // es la fuga que costó una vuelta en la HU #25.
            var recurso = await _caps.FindResourceAsync(TravelSubject.For(item.ProductRef), ct);
            if (!recurso.IsOk)
            {
                return await AbortarAsync(saga, recurso.Rejection!, "el producto no tiene recurso reservable", ct);
            }

            var apartado = await _caps.HoldAsync(
                recurso.Value.Id, ventana, traveller, saga.KeyFor($"hold:{i}"), ct);
            if (!apartado.IsOk)
            {
                return await AbortarAsync(saga, apartado.Rejection!, "un ítem del viaje no está disponible", ct);
            }

            saga = saga with
            {
                Holds = saga.Holds
                    .Append(new ItemHold(apartado.Value.Id, recurso.Value.Id, ventana,
                        item.ProductRef, item.ProductLabel))
                    .ToList(),
                Compensations = saga.Compensations
                    .Append(Compensation.For(
                        ViajesCompensations.ReleaseBookingHold, apartado.Value.Id, "viaje no confirmado"))
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 2. Cuánto cuesta el viaje entero. Una sola cotización: el precio de un paquete no es
        //    necesariamente la suma de sus partes, y preguntarlo por ítem cerraría esa puerta.
        var cotizacion = await _caps.QuoteAsync(
            items.Select(i => TravelSubject.PriceOf(i.ProductRef)).ToList(), ct);
        if (!cotizacion.IsOk)
        {
            return await AbortarAsync(saga, cotizacion.Rejection!, "no se pudo cotizar el viaje", ct);
        }

        var total = Money.Of(cotizacion.Value.Total.Amount, cotizacion.Value.Total.Currency);
        saga = saga with { Total = total };
        _sagas.Put(saga);

        if (total.IsZero)
        {
            // Un viaje de cero lo rechazaría Payments, y además no hay nada que autorizar. Se
            // deja apartado y listo para confirmar.
            return Result.Ok(saga);
        }

        // 3. Autorizar: reserva cupo en el medio de pago SIN mover plata. Que sea autorización y
        //    no cobro es lo que hace que el caso más común de fallo —el viajero se arrepiente—
        //    no cueste una devolución.
        var pago = await _caps.AuthorizeAsync(
            Ref.Create("viajes.reserva", sagaId), traveller, total, saga.KeyFor("authorize"), ct);
        if (!pago.IsOk)
        {
            return await AbortarAsync(saga, pago.Rejection!, "el cobro no se pudo autorizar", ct);
        }

        saga = saga with
        {
            PaymentId = pago.Value.Id,
            Compensations = saga.Compensations
                .Append(Compensation.For(ViajesCompensations.VoidPayment, pago.Value.Id, "viaje no confirmado"))
                .ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>
    /// Fase 2: captura el cobro y confirma cada apartado. Si algo falla, deshace.
    /// </summary>
    public async Task<Result<TripSaga>> ConfirmAsync(string sagaId, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null) return Rejection.NotFound("viajes.trip_not_found", $"No existe el viaje {sagaId}.");
        if (saga.Status == SagaStatus.Completed) return Result.Ok(saga);   // idempotente
        if (saga.Status != SagaStatus.Running)
        {
            return Rejection.Conflict("viajes.not_confirmable", $"El viaje está {saga.Status}.");
        }

        // 1. Capturar. A partir de acá hay plata movida, y todo fallo cuesta una devolución.
        if (saga.PaymentId is { } paymentId)
        {
            var capturado = await _caps.CaptureAsync(paymentId, saga.KeyFor("capture"), ct);
            if (!capturado.IsOk)
            {
                return await AbortarAsync(saga, capturado.Rejection!, "el cobro no se pudo capturar", ct);
            }

            // «Liberar la autorización» pasa a ser «devolver»: ya se movió plata, y liberar una
            // autorización capturada Payments lo rechaza. Sin este cambio la compensación
            // fallaría siempre y quedaría colgada para siempre.
            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == ViajesCompensations.VoidPayment && c.IsPending
                        ? c with { Kind = ViajesCompensations.RefundPayment }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 2. Confirmar cada apartado. Es lo que cierra la puerta, así que va lo más tarde posible
        //    (`feedback_close_doors_last`) — y la reescritura va DENTRO del bucle: cada ítem deja
        //    de ser reversible por la vía barata en el instante en que se confirma, no al final.
        for (var i = 0; i < saga.Holds.Count; i++)
        {
            var apartado = saga.Holds[i];
            if (apartado.ReservationId is not null) continue;   // ya confirmado en un intento previo

            var reserva = await _caps.ConfirmHoldAsync(apartado.HoldId, saga.KeyFor($"confirm:{i}"), ct);
            if (!reserva.IsOk)
            {
                if (!saga.PartialConfirm)
                {
                    _log.LogWarning("El viaje {Saga} no pudo confirmar '{Producto}' tras capturar ({Error}); se compensa.",
                        saga.Id, apartado.ProductRef, reserva.Rejection);
                    return await AbortarAsync(saga, reserva.Rejection!, "un ítem no se pudo confirmar tras cobrar", ct);
                }

                // CONFIRMACIÓN PARCIAL (#40): lo que sí salió se queda. Quien compró un vuelo, un
                // hotel y un auto no pierde el vuelo porque el auto se agotó — y tumbar el viaje
                // entero sería peor servicio y más plata moviéndose sin necesidad.
                //
                // Lo que NO se hace acá es devolver plata. El precio se cotiza UNA vez por viaje
                // a propósito («el precio de un paquete no es necesariamente la suma de sus
                // partes»), así que este orquestador no sabe cuánto vale el ítem caído — y
                // repartir el total sería inventarse una política comercial. Se reporta qué no se
                // cumplió y quien vendió ordena la devolución, exactamente igual que la penalidad
                // de `CancelAsync` llega calculada de fuera.
                _log.LogWarning("El viaje {Saga} no pudo confirmar '{Producto}' ({Error}); sigue PARCIAL.",
                    saga.Id, apartado.ProductRef, reserva.Rejection);

                saga = await SoltarNoCumplidoAsync(saga, i, ct);
                continue;
            }

            var holds = saga.Holds.ToList();
            holds[i] = apartado with { ReservationId = reserva.Value.Id };

            saga = saga with
            {
                Holds = holds,
                Compensations = saga.Compensations
                    .Select(c => c.Kind == ViajesCompensations.ReleaseBookingHold
                              && c.TargetId == apartado.HoldId && c.IsPending
                        ? c with { Kind = ViajesCompensations.CancelReservation, TargetId = reserva.Value.Id }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // Un viaje parcial en el que no se cumplió NADA no es un viaje parcial: es un viaje
        // fallido, y se trata como tal. Dejarlo Completed diría que se entregó algo.
        if (saga.PartialConfirm && saga.Holds.Count > 0 && saga.Holds.All(h => h.Unfulfilled))
        {
            return await AbortarAsync(saga,
                Rejection.Conflict("viajes.nothing_fulfilled", "Ningún ítem del viaje se pudo confirmar."),
                "ningún ítem se pudo confirmar tras cobrar", ct);
        }

        // Salió entero: ya no hay nada que deshacer. Las armadas se marcan como no aplicables —
        // que es distinto de ejecutarlas (`feedback_compensation_is_data`).
        //
        // CON UNA EXCEPCIÓN, y la destapó un test antes de que llegara a ningún lado: el apartado
        // de un ítem no cumplido que NO se pudo soltar sigue siendo trabajo de verdad. Barrerlo
        // acá porque «el viaje salió» perdería ese cupo en silencio — el hotel acabaría lleno de
        // reservas que nadie hizo, y nada habría fallado.
        var sinSoltar = saga.Holds
            .Where(h => h.Unfulfilled)
            .Select(h => h.HoldId)
            .ToHashSet(StringComparer.Ordinal);

        var ahora = _clock.GetUtcNow();
        saga = saga with
        {
            Status = SagaStatus.Completed,
            Compensations = saga.Compensations
                .Select(c => c.IsPending
                             && !(c.Kind == ViajesCompensations.ReleaseBookingHold && sinSoltar.Contains(c.TargetId))
                    ? c with { DoneAtUtc = ahora }
                    : c)
                .ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>
    /// Marca un ítem como no cumplido y suelta su apartado, sin tumbar el viaje.
    /// </summary>
    /// <remarks>
    /// <para><b>Se suelta AQUÍ y no al final</b>: el viaje va a terminar en <c>Completed</c>, y
    /// completar marca las compensaciones armadas como no aplicables. Dejarlo para entonces
    /// convertiría el apartado de este ítem en cupo retenido que ya no lo suelta nadie.</para>
    ///
    /// <para><b>Si soltarlo falla, su compensación se queda PENDIENTE</b> a propósito, para que el
    /// barrido lo reintente. Marcarla hecha porque «el viaje siguió» perdería el cupo en silencio,
    /// que es la clase de fallo que no rompe nada y se descubre cuando el hotel está lleno de
    /// reservas que nadie hizo.</para>
    /// </remarks>
    private async Task<TripSaga> SoltarNoCumplidoAsync(TripSaga saga, int indice, CancellationToken ct)
    {
        var apartado = saga.Holds[indice];
        var soltado = await _caps.ReleaseHoldAsync(apartado.HoldId, ct);

        var holds = saga.Holds.ToList();
        holds[indice] = apartado with { Unfulfilled = true };

        var ahora = _clock.GetUtcNow();
        saga = saga with
        {
            Holds = holds,
            Compensations = saga.Compensations
                .Select(c => soltado.IsOk
                             && c.Kind == ViajesCompensations.ReleaseBookingHold
                             && c.TargetId == apartado.HoldId && c.IsPending
                    ? c with { DoneAtUtc = ahora }
                    : c)
                .ToList(),
        };

        if (!soltado.IsOk)
        {
            _log.LogWarning("El viaje {Saga} no pudo soltar el apartado de '{Producto}' ({Error}); queda para el barrido.",
                saga.Id, apartado.ProductRef, soltado.Rejection);
        }

        _sagas.Put(saga);
        return saga;
    }

    /// <summary>
    /// Devuelve una PARTE de lo cobrado por un viaje que salió a medias.
    /// </summary>
    /// <remarks>
    /// <para><b>Es la otra mitad de la confirmación parcial</b> (#40). Al conservar lo que sí
    /// salió, este orquestador no devuelve nada por su cuenta y lo dice de frente: el precio se
    /// cotiza <i>una vez por viaje</i>, así que acá no se sabe cuánto vale el ítem caído, y
    /// repartir el total sería inventarse una política comercial. Sin esta puerta, «quien vendió
    /// ordena la devolución» era una frase sin forma de cumplirse — y la plata del ítem no
    /// entregado se quedaba con nosotros.</para>
    ///
    /// <para><b>El monto llega calculado de fuera</b>, exactamente igual que la penalidad de
    /// <see cref="CancelAsync"/> y por la misma razón. Lo único que se comprueba acá es que quepa
    /// en el viaje; cuánto queda devolvible de verdad lo decide <c>Api.Payments</c>, que es su
    /// dueño.</para>
    ///
    /// <para><b>Si la devolución falla NO se anota una compensación</b>, y no es un olvido: el
    /// motor sólo sabe devolver <i>todo lo devolvible</i> (<c>RefundPayment</c>), y correrlo acá
    /// devolvería un viaje que SÍ se entregó. El rechazo sale hacia quien vendió, que es quien
    /// puede repetir la orden con la misma llave sin duplicar nada.</para>
    ///
    /// <para><b>Y lo devuelto se ESPEJA de la capacidad, no se acumula acá.</b> Repetir la misma
    /// llave devuelve la misma operación de <c>Api.Payments</c>; sumarla de este lado la contaría
    /// dos veces y el viaje diría haber devuelto el doble de lo que devolvió.</para>
    /// </remarks>
    public async Task<Result<TripSaga>> RefundAsync(
        string sagaId, Money amount, string? reason, IdempotencyKey key, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null) return Rejection.NotFound("viajes.trip_not_found", $"No existe el viaje {sagaId}.");

        // Sólo sobre un viaje que salió. Antes de confirmar no se movió plata —deshacer es
        // liberar la autorización, no devolver— y sobre uno ya compensado la devolución ya la
        // ordenó la máquina.
        if (saga.Status != SagaStatus.Completed)
        {
            return Rejection.Conflict("viajes.not_refundable",
                $"El viaje está {saga.Status}: sólo se devuelve una parte de un viaje confirmado.");
        }

        if (saga.PaymentId is not { } pago)
        {
            return Rejection.Conflict("viajes.nothing_to_refund",
                "El viaje no movió plata: no hay nada que devolver.");
        }

        if (amount.IsZero || amount.IsNegative)
        {
            return Rejection.Invalid("viajes.bad_refund", $"No se puede devolver {amount}.");
        }

        if (!string.Equals(amount.Currency, saga.Total.Currency, StringComparison.Ordinal))
        {
            return Rejection.Invalid("viajes.bad_refund",
                $"Se pidió devolver {amount} de un viaje en {saga.Total.Currency}.");
        }

        if (amount > saga.Total)
        {
            return Rejection.Invalid("viajes.bad_refund",
                $"Se pidió devolver {amount} de un viaje de {saga.Total}.");
        }

        var r = await _caps.RefundAsync(pago, amount, reason ?? "ítem del viaje no cumplido", key, ct);
        if (!r.IsOk)
        {
            _log.LogError("El viaje {Saga} no pudo devolver {Monto}: {Error}", saga.Id, amount, r.Rejection);
            return Result.Rejected<TripSaga>(r.Rejection!);
        }

        saga = saga with { Refunded = Devuelto(r.Value, amount) };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>Cuánto lleva devuelto el cobro, según la capacidad que lo guarda.</summary>
    /// <remarks>
    /// El respaldo —lo que se acaba de pedir— es para una capacidad que no informe el acumulado.
    /// Es peor dato que el suyo, pero mucho mejor que decir que no se devolvió nada.
    /// </remarks>
    private static Money Devuelto(PaymentDto pago, Money pedido)
        => pago.Refunded is { } d ? Money.Of(d.Amount, d.Currency) : pedido;

    /// <summary>
    /// El viajero se arrepiente: se deshace lo hecho, reteniendo lo que diga la política.
    /// </summary>
    /// <remarks>
    /// <para><b>La penalidad LLEGA calculada, no se calcula acá.</b> Cuánto se retiene depende de
    /// la tarifa y de cuántos días falten, y eso lo sabe quien vendió — no un orquestador que
    /// sirve a hoteles, vuelos y autos con políticas distintas. Acá solo se ordena devolver el
    /// resto, que es exactamente lo que <c>Api.Booking</c> dice que le toca al BFF: «Booking dice
    /// si la cancelación está en plazo; qué se devuelve y a quién lo decide Api.Payments y lo
    /// ORDENA el BFF».</para>
    ///
    /// <para><b>Retener sin haber capturado no tiene sentido</b> y no se intenta: antes de la
    /// confirmación no se movió plata, así que deshacer es liberar la autorización entera. La
    /// penalidad solo muerde sobre lo ya cobrado.</para>
    /// </remarks>
    public async Task<Result<TripSaga>> CancelAsync(string sagaId, Money? retain, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null) return Rejection.NotFound("viajes.trip_not_found", $"No existe el viaje {sagaId}.");
        if (saga.Status == SagaStatus.Compensated) return Result.Ok(saga);   // idempotente

        if (retain is { } penalidad && !penalidad.IsZero)
        {
            if (penalidad.Amount < 0m || penalidad.Amount > saga.Total.Amount)
            {
                return Rejection.Invalid("viajes.bad_penalty",
                    $"La penalidad {penalidad.Amount} no cabe en un viaje de {saga.Total.Amount}.");
            }
            saga = saga with { Retained = penalidad };
            _sagas.Put(saga);
        }

        // Un viaje YA CONFIRMADO no se deshace compensando, y no es un detalle: `Bff.Core` lo
        // rechaza con todas las letras —«deshacerlo es una cancelación con su política, no una
        // compensación»— y tiene razón. Compensar es lo que se hace cuando un paso falló a la
        // mitad; esto es un acto del viajero sobre algo que salió bien.
        if (saga.Status == SagaStatus.Completed)
        {
            return await CancelarConfirmadoAsync(saga, ct);
        }

        await _sagas.CompensateAsync(saga.Id, "el viajero canceló", ct);
        return Result.Ok(_sagas.Find(sagaId)!);
    }

    /// <summary>
    /// Cancela un viaje que ya estaba confirmado: suelta las reservas y devuelve lo que toque.
    /// </summary>
    /// <remarks>
    /// <para><b>El orden es el inverso del de confirmar, y por la misma razón.</b> Primero se
    /// sueltan las reservas —que es reversible: se pueden volver a tomar si quedan— y al final se
    /// devuelve la plata, que es lo que no se puede deshacer. Al revés, un fallo soltando dejaría
    /// al viajero con la devolución hecha y el cupo todavía ocupado.</para>
    ///
    /// <para><b>Y si la devolución falla, se ANOTA como pendiente</b> en vez de perderse: el
    /// barrido la reintenta con su retroceso, y si se rinde avisa a una persona. Es el único
    /// trozo de esto que necesita la máquina, porque es el único que no se puede dejar a medias
    /// sin que alguien pierda plata.</para>
    /// </remarks>
    private async Task<Result<TripSaga>> CancelarConfirmadoAsync(TripSaga saga, CancellationToken ct)
    {
        var fallos = new List<string>();

        foreach (var apartado in saga.Holds)
        {
            if (apartado.ReservationId is not { } reserva) continue;

            var r = await _caps.CancelReservationAsync(reserva, ct);
            if (!r.IsOk)
            {
                // No se corta: soltar las demás sigue valiendo la pena, y lo que no se pudo
                // soltar queda dicho para que una persona lo vea.
                _log.LogError("El viaje {Saga} no pudo cancelar la reserva {Reserva}: {Error}",
                    saga.Id, reserva, r.Rejection);
                fallos.Add($"{apartado.ProductRef}: {r.Rejection}");
            }
        }

        var compensaciones = saga.Compensations.ToList();
        var devuelto = saga.Refunded;

        if (saga.PaymentId is { } pago)
        {
            var estado = await _caps.GetPaymentAsync(pago, ct);
            var devolvible = estado.IsOk
                ? Money.Of(estado.Value.Refundable.Amount, estado.Value.Refundable.Currency)
                : Money.Zero(saga.Total.Currency);

            var aDevolver = saga.Retained.IsZero || saga.Retained.Amount >= devolvible.Amount
                ? devolvible
                : Money.Of(devolvible.Amount - saga.Retained.Amount, devolvible.Currency);

            if (!aDevolver.IsZero)
            {
                var r = await _caps.RefundAsync(pago, aDevolver, "el viajero canceló",
                    saga.KeyFor("cancel-refund"), ct);
                if (r.IsOk)
                {
                    // Cuánto volvió, para que quien vendió lo pueda decir sin preguntarle a
                    // Payments —a la que no puede llamar— y sin deducirlo del total, que sería
                    // adivinar: lo devuelto no es el total menos la penalidad cuando ya se había
                    // devuelto algo antes por un ítem no cumplido.
                    devuelto = Devuelto(r.Value, aDevolver);
                }
                else
                {
                    // Se anota y el barrido se encarga. Perder una devolución en un log es el
                    // defecto que la máquina de compensación existe para impedir.
                    _log.LogError("El viaje {Saga} canceló las reservas pero la devolución falló: {Error}",
                        saga.Id, r.Rejection);
                    compensaciones.Add(Compensation.For(
                        ViajesCompensations.RefundPayment, pago, "el viajero canceló"));
                    fallos.Add($"devolución: {r.Rejection}");
                }
            }
        }

        var cancelada = saga with
        {
            Status = SagaStatus.Compensated,
            Compensations = compensaciones,
            Refunded = devuelto,
            LastError = fallos.Count == 0 ? null : string.Join(" · ", fallos),
        };
        _sagas.Put(cancelada);
        return Result.Ok(cancelada);
    }

    /// <summary>Volver a intentar lo que se rindió. Es la puerta de la persona a la que se avisó.</summary>
    public async Task<Result<TripSaga>> RetryStuckAsync(string sagaId, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null) return Rejection.NotFound("viajes.trip_not_found", $"No existe el viaje {sagaId}.");

        await _sagas.RetryStuckAsync(saga.Id, ct);
        return Result.Ok(_sagas.Find(sagaId)!);
    }

    public Result<TripSaga> Get(string sagaId)
        => _sagas.Find(sagaId) is { } s
            ? Result.Ok(s)
            : Rejection.NotFound("viajes.trip_not_found", $"No existe el viaje {sagaId}.");

    public IReadOnlyList<TripSaga> PendingCompensations() => _sagas.PendingCompensations();

    /// <summary>
    /// Anota el motivo, deshace lo hecho y devuelve el rechazo original.
    /// </summary>
    /// <remarks>
    /// <b>El rechazo que sale es el de la capacidad, no uno propio.</b> «No hay cupo en el hotel»
    /// es accionable para quien reserva; «no se pudo reservar el viaje» no lo es, y taparlo con
    /// un código del orquestador convierte cuatro motivos distintos en uno inútil.
    /// </remarks>
    private async Task<Result<TripSaga>> AbortarAsync(
        TripSaga saga, Rejection motivo, string porQue, CancellationToken ct)
    {
        _sagas.Put(saga with { LastError = motivo.ToString() });
        await _sagas.CompensateAsync(saga.Id, porQue, ct);
        return Result.Rejected<TripSaga>(motivo);
    }

    /// <summary>Lo que se rechaza antes de tocar ninguna capacidad.</summary>
    private static Rejection? Revisar(IReadOnlyList<TripItem> items)
    {
        if (items is null || items.Count == 0)
        {
            return Rejection.Invalid("viajes.no_items", "Un viaje necesita al menos un ítem.");
        }
        if (items.Count > MaxItems)
        {
            return Rejection.Invalid("viajes.too_many_items", $"Un viaje admite hasta {MaxItems} ítems.");
        }

        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ProductRef))
            {
                return Rejection.Invalid("viajes.bad_product", "Cada ítem necesita su producto.");
            }
            // Una ventana que no avanza no es «nada reservado», es un error de quien la
            // construyó — y acá se contesta con un rechazo, no con una excepción: viene de una
            // petición, no de un defecto nuestro.
            if (item.End <= item.Start)
            {
                return Rejection.Invalid("viajes.bad_window",
                    $"El periodo de '{item.ProductRef}' no avanza: {item.Start:O} → {item.End:O}.");
            }
            // Dos veces el mismo producto EN EL MISMO PERIODO es un error de quien pide, no una
            // reserva doble legítima: el segundo apartado competiría con el primero por el mismo
            // cupo. Dos periodos distintos del mismo producto sí valen — dos tramos de vuelo.
            if (!vistos.Add($"{item.ProductRef}|{item.Start:O}|{item.End:O}"))
            {
                return Rejection.Invalid("viajes.duplicate_item",
                    $"'{item.ProductRef}' viene dos veces con el mismo periodo.");
            }
        }
        return null;
    }
}
