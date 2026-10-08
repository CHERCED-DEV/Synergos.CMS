using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Bff.Eventos.Clients;
using Synergos.Bff.Pasos;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>Una línea de la compra: qué localidad, qué butaca si la hay, y cuántas.</summary>
/// <remarks>
/// <b>El precio NO está acá</b>, y es deliberado. Si viajara en la petición, cualquiera compraría
/// la localidad VIP al precio de la general cambiando un número. Se cotiza contra
/// <c>Api.Pricing</c> y lo que el llamador mandó no se mira.
/// </remarks>
public sealed record TicketLine(string Tier, string? Seat, int Quantity);

/// <summary>
/// Comprar entradas — <b>la puerta del flujo <c>eventos.compra</c></b>, que ya no está escrito acá.
/// </summary>
/// <remarks>
/// <para><b>Tres capacidades y ninguna sabe que existe un evento.</b> Pricing sabe de precios,
/// Inventory de pozos contables y Payments de plata. Que el aforo se aparta ANTES de cobrar, que
/// la butaca se consume DESPUÉS de capturar, y que si algo falla a la mitad hay que devolver
/// las dos cosas — eso no lo sabe ninguna. Desde la ADR 0140 lo dice
/// <c>flujos/eventos.compra.json</c> y lo ejecuta <see cref="FlowRunner{TSaga}"/>; esta clase
/// conserva su forma porque es la que llaman los endpoints y los tests de la compra.</para>
///
/// <para><b>Las dos fases, otra vez.</b> Comprar aparta y <i>autoriza</i>; confirmar
/// <i>captura</i> y consume. El caso más común de fallo —el comprador se arrepiente, la tarjeta
/// rechaza, el apartado se vence— no cuesta una devolución.</para>
///
/// <para><b>Y el orden dentro de confirmar</b> sigue la regla que enseñó una corrida real en
/// Tienda: <i>lo que cierra una puerta va lo más tarde posible</i>. Se captura primero —el fallo
/// que deja plata cobrada sin butaca se deshace solo; el que deja butaca entregada sin cobrar exige
/// perseguir a una persona— y se consume después. Ese orden es ahora el de la fase
/// <c>cerrar</c> de la definición.</para>
///
/// <para><b>Lo que este dominio NO comparte con la tienda:</b> no hay pedido ni despacho. Una
/// entrada no se envía. El artefacto —el e-ticket con su QR— lo emite el CMS después de que esto
/// conteste que sí, porque el firmante vive allá y un QR no es cupo ni es plata.</para>
///
/// <para><b>Lo que se queda acá, y por qué:</b> la llave (<c>Abrir</c>, del motor, antes de tocar
/// nada) y las guardas de confirmar, que contestan con los códigos de ESTE dominio
/// (<c>eventos.purchase_not_found</c>, <c>eventos.not_confirmable</c>). El intérprete no los
/// conoce, y no tiene por qué.</para>
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

    private const string Abrir = "abrir";
    private const string Cerrar = "cerrar";

    /// <summary>
    /// Las fases de <c>eventos.compra</c> que esta fachada invoca, en orden: comprar abre y
    /// confirmar cierra.
    /// </summary>
    /// <remarks>
    /// Se declaran al registrar el flujo (<c>Program.cs</c>) y al armar el intérprete, y las dos
    /// cosas validan contra la definición: renombrar una fase en el JSON, repetirla o reordenarla
    /// no arranca, en vez de lanzar en la primera confirmación.
    /// </remarks>
    public static IReadOnlyList<string> Fases { get; } = new[] { Abrir, Cerrar };

    private readonly SagaEngine<TicketingSaga> _sagas;
    private readonly FlowRunner<TicketingSaga> _flujo;

    /// <remarks>
    /// <b>La firma es la de siempre y arma el intérprete por dentro</b>: los tests de la compra la
    /// construyen a mano, sin contenedor, y tienen que seguir pasando sin tocarlos. Si la definición
    /// no valida contra estos pasos, este binding y estas fases, construirla lanza — igual que el
    /// arranque con <c>ValidateOnStart</c>, que valida lo mismo.
    /// </remarks>
    public TicketingFlow(
        EventosCapabilities caps, SagaEngine<TicketingSaga> sagas,
        TimeProvider clock, ILogger<TicketingFlow> log)
    {
        _sagas = sagas;
        _flujo = new FlowRunner<TicketingSaga>(
            sagas, EventosPasos.Registro(caps), EventosFlujos.Compra, new EventosFlowBinding(), Fases, clock, log);
    }

    /// <summary>Aparta el aforo y autoriza el cobro, sin comisión de servicio.</summary>
    public Task<Result<TicketingSaga>> BuyAsync(
        string eventId, Ref buyer, IReadOnlyList<TicketLine> lineas, string sagaId, CancellationToken ct)
        => BuyAsync(eventId, buyer, lineas, 0m, sagaId, ct);

    /// <summary>Aparta el aforo y autoriza el cobro. No mueve plata todavía.</summary>
    /// <param name="comisionPorcentaje">La comisión de servicio sobre el subtotal, de 0 a 100 y con
    /// dos decimales como mucho: la que el CMS le mostró al comprador (ADR 0137).</param>
    public async Task<Result<TicketingSaga>> BuyAsync(
        string eventId, Ref buyer, IReadOnlyList<TicketLine> lineas, decimal comisionPorcentaje,
        string sagaId, CancellationToken ct)
    {
        // La saga existe ANTES de tocar nada. Si el proceso se cae después del primer paso, lo
        // que se hizo queda escrito con su identificador — y como las llaves derivan de él,
        // repetir la llamada con el mismo sagaId no duplica nada.
        //
        // Pero encontrar la llave NO siempre significa «esto ya pasó»: si la compra anterior se
        // deshizo entera, no queda nada que duplicar y el comprador tiene derecho a reintentar.
        // Quién decide eso vive en el motor, no acá — estas líneas estaban copiadas en los dos
        // orquestadores y el defecto #41 también (encerraba al comprador para siempre).
        var slot = _sagas.Abrir(sagaId);
        if (slot.Reusar is not null) return Result.Ok(slot.Reusar);

        return await _flujo.EjecutarFaseAsync(
            Abrir, slot.Id, EventosFlowBinding.Entrada(eventId, buyer, lineas, comisionPorcentaje), ct);
    }

    /// <summary>Captura el cobro y consume el aforo, sin avisar a nadie.</summary>
    public Task<Result<TicketingSaga>> ConfirmAsync(string sagaId, CancellationToken ct)
        => ConfirmAsync(sagaId, null, ct);

    /// <summary>Captura el cobro, consume el aforo y avisa al comprador. A partir de acá hay plata movida.</summary>
    /// <param name="sagaId">La compra.</param>
    /// <param name="contacto">Adónde avisar, que pone la puerta: entra como efímero de «cerrar» y
    /// no se guarda. Nulo si nadie pidió el aviso, y entonces el paso no avisa.</param>
    /// <param name="ct">Cancelación.</param>
    public async Task<Result<TicketingSaga>> ConfirmAsync(string sagaId, Contacto? contacto, CancellationToken ct)
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

        // NO se emite el e-ticket al terminar, y no es un olvido: el QR lo firma el CMS, que es
        // donde vive el firmante. Un orquestador que emitiera artefactos tendría estado propio más
        // allá de sus sagas, y entonces sería una capacidad mal cortada.
        return await _flujo.EjecutarFaseAsync(Cerrar, sagaId, EventosFlowBinding.EntradaDeCerrar(contacto), ct);
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
    /// <remarks>Lo ejecuta el paso <c>eventos.revisar-lineas</c>, antes de que exista la saga.</remarks>
    internal static Rejection? Revisar(IReadOnlyList<TicketLine> lineas)
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
}
