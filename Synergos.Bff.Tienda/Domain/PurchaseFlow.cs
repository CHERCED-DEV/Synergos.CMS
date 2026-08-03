using Synergos.Bff.Core;
using Compensation = Synergos.Bff.Core.Compensation;
using Synergos.Bff.Tienda.Clients;
using Synergos.Core;

namespace Synergos.Bff.Tienda.Domain;

/// <summary>
/// El flujo de comprar una canasta — <b>el ORDEN, que es lo del dominio</b>.
/// </summary>
/// <remarks>
/// <para><b>Seis capacidades y ninguna sabe que existe una compra.</b> Cart sabe de líneas,
/// Pricing de precios, Inventory de existencias, Orders de pedidos, Payments de plata y
/// Fulfillment de envíos. Que las existencias se apartan ANTES de cobrar, que el pedido se
/// registra antes de autorizar, y que si el despacho falla hay que devolver — eso no lo sabe
/// ninguna, y es lo que vive acá.</para>
///
/// <para><b>Las dos fases, otra vez, y por lo mismo que en Salud.</b> Comprar aparta y
/// <i>autoriza</i>; confirmar <i>captura</i> y despacha. El caso más común de fallo —el
/// comprador se arrepiente, la tarjeta rechaza, el carrito se vence— no cuesta una devolución.
/// Que dos dominios muy distintos lleguen a la misma forma no es casualidad: es lo que hace que
/// la máquina de sagas sea compartible.</para>
///
/// <para><b>El orden dentro de confirmar:</b> capturar → consumir existencias → despachar →
/// cerrar el pedido → cerrar la canasta. Se captura primero por la misma razón que en Salud: el
/// fallo que deja plata cobrada sin mercancía <b>se deshace solo</b>, y el que deja mercancía
/// despachada sin cobrar exige perseguir a una persona.</para>
///
/// <para><b>Cerrar el pedido va DESPUÉS de despachar, y eso lo enseñó una corrida real.</b>
/// Estaba antes, y cuando el despacho falló, <c>Api.Orders</c> contestó
/// <c>order_closed: el pedido está Fulfilled y no admite más cambios; una devolución no es una
/// cancelación</c> — la compensación quedó colgada para siempre sobre un paso que ella misma
/// había vuelto imposible. La regla que sale de ahí: <b>lo que cierra una puerta va lo más tarde
/// posible</b>, cuando ya no queda nada que pueda fallar detrás.</para>
/// </remarks>
public sealed class PurchaseFlow
{
    /// <summary>Cuántas líneas admite una compra.</summary>
    /// <remarks>
    /// Sin tope, una canasta con mil líneas dispara mil apartados de existencias y mil
    /// compensaciones — y el barrido de una sola saga rendida martillearía Inventory mil veces
    /// por vuelta.
    /// </remarks>
    public const int MaxLines = 50;

    private readonly TiendaCapabilities _caps;
    private readonly SagaEngine<PurchaseSaga> _sagas;
    private readonly TimeProvider _clock;
    private readonly ILogger<PurchaseFlow> _log;

    public PurchaseFlow(
        TiendaCapabilities caps, SagaEngine<PurchaseSaga> sagas,
        TimeProvider clock, ILogger<PurchaseFlow> log)
    {
        _caps = caps;
        _sagas = sagas;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// Fase 1: lee la canasta, cotiza, aparta existencias, registra el pedido y autoriza el cobro.
    /// </summary>
    public async Task<Result<PurchaseSaga>> BuyAsync(string cartId, string sagaId, CancellationToken ct)
    {
        // La saga existe ANTES de tocar nada. Si el proceso se cae después del primer paso, lo
        // que se hizo queda escrito con su identificador — y como las llaves derivan de él,
        // repetir la llamada con el mismo sagaId no duplica nada.
        var previa = _sagas.Find(sagaId);
        if (previa is not null) return Result.Ok(previa);

        // 1. La canasta. Es la única fuente de qué se está comprando: copiar las líneas en la
        //    petición dejaría que el cliente pidiera algo distinto de lo que tiene en pantalla.
        var cart = await _caps.GetCartAsync(cartId, ct);
        if (!cart.IsOk) return Result.Rejected<PurchaseSaga>(cart.Rejection!);

        var motivo = Revisar(cart.Value);
        if (motivo is not null) return Result.Rejected<PurchaseSaga>(motivo);

        var buyer = Ref.Create(cart.Value.OwnerKind, cart.Value.OwnerId);

        // 2. Cuánto cuesta. Va antes de apartar porque un precio que no se puede cotizar
        //    aborta la compra sin haber tocado el inventario de nadie.
        var quote = await _caps.QuoteAsync(cart.Value.Lines, ct);
        if (!quote.IsOk) return Result.Rejected<PurchaseSaga>(quote.Rejection!);
        var total = Money.Of(quote.Value.Total.Amount, quote.Value.Total.Currency);

        var saga = new PurchaseSaga(sagaId, buyer, cartId, SagaStatus.Running,
            Array.Empty<StockHold>(), null, null, null, total,
            Array.Empty<Compensation>(), null, _clock.GetUtcNow());

        // 3. Apartar existencias, UNA POR LÍNEA. Cada apartado se anota como compensable en el
        //    mismo momento en que existe: si el proceso se cae en la línea cuatro, las tres
        //    primeras ya tienen quién las suelte.
        foreach (var linea in cart.Value.Lines)
        {
            var subject = Ref.Create(linea.SubjectKind, linea.SubjectId);

            var item = await _caps.FindStockAsync(subject, ct);
            if (!item.IsOk) return await AbortarAsync(saga, item.Rejection!, "no hay existencias declaradas", ct);

            // La llave lleva el ítem y no el índice de la línea: si el comprador reordena la
            // canasta entre dos intentos, una llave por posición apartaría dos veces.
            var hold = await _caps.HoldStockAsync(item.Value.Id, linea.Quantity,
                Ref.Create("tienda.compra", sagaId), saga.KeyFor($"hold:{item.Value.Id}"), ct);
            if (!hold.IsOk) return await AbortarAsync(saga, hold.Rejection!, "no se pudo apartar la mercancía", ct);

            saga = saga with
            {
                Holds = saga.Holds.Append(new StockHold(hold.Value.Id, item.Value.Id, linea.Quantity)).ToList(),
                Compensations = saga.Compensations
                    .Append(Compensation.For(TiendaCompensations.ReleaseStockHold, hold.Value.Id, "compra no confirmada"))
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 4. El pedido. Se registra ANTES de cobrar para que el cobro tenga a qué referirse:
        //    un movimiento de plata sin pedido al lado no se puede conciliar.
        var lineas = cart.Value.Lines
            .Select(l => (Line: l, UnitPrice: Money.Zero(total.Currency)))
            .ToList();

        var order = await _caps.PlaceOrderAsync(buyer, lineas, total, saga.KeyFor("order"), ct);
        if (!order.IsOk) return await AbortarAsync(saga, order.Rejection!, "no se pudo registrar el pedido", ct);

        saga = saga with
        {
            OrderId = order.Value.Id,
            Compensations = saga.Compensations
                .Append(Compensation.For(TiendaCompensations.CancelOrder, order.Value.Id, "compra no confirmada"))
                .ToList(),
        };
        _sagas.Put(saga);

        // 5. Autorizar: reserva cupo en el medio de pago SIN mover plata.
        var pago = await _caps.AuthorizeAsync(Ref.Create("tienda.pedido", order.Value.Id), buyer, total,
            saga.KeyFor("authorize"), ct);
        if (!pago.IsOk) return await AbortarAsync(saga, pago.Rejection!, "el cobro no se pudo autorizar", ct);

        saga = saga with
        {
            PaymentId = pago.Value.Id,
            Compensations = saga.Compensations
                .Append(Compensation.For(TiendaCompensations.VoidPayment, pago.Value.Id, "compra no confirmada"))
                .ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>
    /// Fase 2: captura, consume las existencias, cierra el pedido, despacha y cierra la canasta.
    /// </summary>
    public async Task<Result<PurchaseSaga>> ConfirmAsync(string sagaId, object address, string carrier, CancellationToken ct)
    {
        var saga = _sagas.Find(sagaId);
        if (saga is null)
        {
            return Rejection.NotFound("tienda.purchase_not_found", $"No existe la compra {sagaId}.");
        }
        if (saga.Status == SagaStatus.Completed) return Result.Ok(saga);   // idempotente
        if (saga.Status != SagaStatus.Running)
        {
            return Rejection.Conflict("tienda.not_confirmable", $"La compra está {saga.Status}.");
        }

        // 1. Capturar. A partir de acá hay plata movida, y todo fallo cuesta una devolución.
        if (saga.PaymentId is { } paymentId)
        {
            var capturado = await _caps.CaptureAsync(paymentId, saga.KeyFor("capture"), ct);
            if (!capturado.IsOk) return await AbortarAsync(saga, capturado.Rejection!, "el cobro no se pudo capturar", ct);

            // La compensación del pago pasa de "liberar autorización" a "devolver": ya se movió
            // plata, y liberar una autorización capturada Payments lo rechaza. Sin este cambio,
            // la compensación fallaría siempre y quedaría colgada para siempre.
            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == TiendaCompensations.VoidPayment && c.IsPending
                        ? c with { Kind = TiendaCompensations.RefundPayment }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 2. Consumir las existencias, UNA POR APARTADO. Cada consumo reescribe su propia
        //    compensación en el acto: soltar un apartado ya consumido lo rechaza Api.Inventory
        //    —"devolver existencias es un ajuste, no una liberación"—, así que a partir de acá
        //    deshacer significa devolver unidades al ítem. Reescribirla DENTRO del bucle y no
        //    al final importa: si el consumo falla en la línea tres, las dos primeras ya están
        //    consumidas y su compensación tiene que ser la buena.
        foreach (var hold in saga.Holds)
        {
            var consumido = await _caps.ConsumeStockAsync(hold.HoldId, ct);
            if (!consumido.IsOk) return await AbortarAsync(saga, consumido.Rejection!, "no se pudo consumir la mercancía apartada", ct);

            saga = saga with
            {
                Compensations = saga.Compensations
                    .Select(c => c.Kind == TiendaCompensations.ReleaseStockHold && c.TargetId == hold.HoldId && c.IsPending
                        ? c with { Kind = TiendaCompensations.RestockItem, TargetId = hold.ItemId }
                        : c)
                    .ToList(),
            };
            _sagas.Put(saga);
        }

        // 3. Despachar. Es el paso que puede dejar plata cobrada sin envío — y el último que
        //    puede fallar teniendo todavía todo compensable detrás.
        var envio = await _caps.ShipAsync(Ref.Create("tienda.pedido", saga.OrderId!), address, carrier,
            saga.KeyFor("ship"), ct);
        if (!envio.IsOk)
        {
            _log.LogWarning("La compra {Saga} no se pudo despachar tras cobrar ({Error}); se compensa.",
                saga.Id, envio.Rejection);
            return await AbortarAsync(saga, envio.Rejection!, "no se pudo despachar tras cobrar", ct);
        }

        // 4. Cerrar el pedido. VA DE PENÚLTIMO a propósito: cerrarlo es lo que le quita a
        //    Api.Orders la posibilidad de anularlo, así que hacerlo antes de despachar dejaba la
        //    compensación CancelOrder condenada a fallar para siempre. Si falla acá no se
        //    compensa la compra: la plata está cobrada, la mercancía salió y el envío existe —
        //    el estado del pedido es contabilidad, y deshacer un despacho por eso no existe.
        var pedido = await _caps.FulfillOrderAsync(saga.OrderId!, ct);
        if (!pedido.IsOk)
        {
            _log.LogError("La compra {Saga} se despachó pero el pedido {Order} no cerró ({Error}). Queda para conciliar.",
                saga.Id, saga.OrderId, pedido.Rejection);
        }

        // 5. Cerrar la canasta. De última, y a propósito: es lo único irreversible para el
        //    comprador. Si algo de lo anterior falla, su canasta sigue ahí para reintentar.
        var cerrada = await _caps.CheckoutCartAsync(saga.CartId, saga.KeyFor("checkout"), ct);
        if (!cerrada.IsOk)
        {
            // No se compensa la compra entera por esto: la mercancía salió, el pedido está
            // cerrado y el envío existe. Una canasta que quedó abierta es un residuo cosmético
            // que su propio TTL barre — deshacer un despacho por eso sería desproporcionado.
            _log.LogWarning("La compra {Saga} se completó pero la canasta {Cart} no cerró ({Error}).",
                saga.Id, saga.CartId, cerrada.Rejection);
        }

        // Salió: ya no hay nada que deshacer. Las compensaciones se marcan como no aplicables
        // —hechas— para que el barrido no las intente.
        var ahora = _clock.GetUtcNow();
        saga = saga with
        {
            Status = SagaStatus.Completed,
            ShipmentId = envio.Value.Id,
            LastError = null,
            Compensations = saga.Compensations.Select(c => c.IsPending ? c with { DoneAtUtc = ahora } : c).ToList(),
        };
        _sagas.Put(saga);
        return Result.Ok(saga);
    }

    /// <summary>Cancela una compra todavía sin confirmar.</summary>
    public Task<Result<PurchaseSaga>> CancelAsync(string sagaId, CancellationToken ct)
        => _sagas.CompensateAsync(sagaId, "cancelada por el comprador", ct);

    /// <summary>Vuelve a intentar lo que se había rendido.</summary>
    public Task<Result<PurchaseSaga>> RetryStuckAsync(string sagaId, CancellationToken ct)
        => _sagas.RetryStuckAsync(sagaId, ct);

    public Result<PurchaseSaga> Get(string id)
        => _sagas.Find(id) is { } s
            ? Result.Ok(s)
            : Rejection.NotFound("tienda.purchase_not_found", $"No existe la compra {id}.");

    public IReadOnlyList<PurchaseSaga> PendingCompensations() => _sagas.PendingCompensations();

    /// <summary>Lo que una canasta tiene que cumplir para poder comprarse.</summary>
    private static Rejection? Revisar(CartDto cart)
    {
        if (cart.CheckedOut || !cart.Open)
        {
            return Rejection.Conflict("tienda.cart_closed", "Esa canasta ya se cerró.");
        }
        if (cart.Lines.Count == 0)
        {
            return Rejection.Invalid("tienda.cart_empty", "No se puede comprar una canasta vacía.");
        }
        if (cart.Lines.Count > MaxLines)
        {
            return Rejection.Invalid("tienda.cart_too_large",
                $"Una compra admite hasta {MaxLines} líneas y esa canasta trae {cart.Lines.Count}.");
        }
        return null;
    }

    /// <summary>
    /// Anota el fallo, deshace lo que ya se hizo, y devuelve el rechazo original.
    /// </summary>
    /// <remarks>
    /// <b>Se devuelve el rechazo de la capacidad y no uno propio</b>: quien llamó necesita saber
    /// si fue <c>inventory.out_of_stock</c> —ofrecer menos unidades— o
    /// <c>payments.declined</c> —pedir otro medio de pago—, y aplanarlos a «no se pudo comprar»
    /// deja al cliente sin nada que hacer.
    /// </remarks>
    private async Task<Result<PurchaseSaga>> AbortarAsync(
        PurchaseSaga saga, Rejection motivo, string razon, CancellationToken ct)
    {
        _sagas.Put(saga with { LastError = motivo.ToString() });
        await _sagas.CompensateAsync(saga.Id, razon, ct);
        return Result.Rejected<PurchaseSaga>(motivo);
    }
}
