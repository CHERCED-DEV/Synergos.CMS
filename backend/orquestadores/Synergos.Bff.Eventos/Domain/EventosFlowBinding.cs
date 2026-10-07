using Synergos.Bff.Core;
using Synergos.Bff.Core.Flow;
using Synergos.Core;

namespace Synergos.Bff.Eventos.Domain;

/// <summary>
/// Cómo la compra de entradas guarda en <see cref="TicketingSaga"/> lo que su flujo declarado hace.
/// </summary>
/// <remarks>
/// <para><b>Es el único sitio que conoce los NOMBRES del contexto</b> —los de
/// <c>flujos/eventos.compra.json</c>— y la forma de la saga a la vez. Y los DECLARA
/// (<see cref="Reconstruye"/>, <see cref="CamposDeItem"/>, <see cref="Reservas"/>): si un nombre
/// cambia en la definición sin cambiar acá, o al revés, el orquestador no arranca.</para>
///
/// <para><b>Los campos que se guardan son los de siempre</b>: el apartado con su localidad y su
/// butaca —que la capacidad no conoce y el CMS sí lee—, el cobro, el total y el error. Nada del
/// contexto llega al disco.</para>
/// </remarks>
public sealed class EventosFlowBinding : IFlowBinding<TicketingSaga>
{
    // Los nombres del contexto que la compra pone y lee.
    internal const string EventId = "eventId";
    internal const string Comprador = "comprador";
    internal const string Lineas = "lineas";
    internal const string Comision = "comisionPct";
    internal const string Total = "total";
    internal const string Cobro = "paymentId";

    // Los campos de cada línea, dentro del bloque que las recorre.
    internal const string Localidad = "tier";
    internal const string Butaca = "seat";
    internal const string Cantidad = "cantidad";

    // Los pasos de la definición que reservan, que es por lo que la saga guarda lo reservado.
    internal const string Apartar = "apartar";
    internal const string Autorizar = "autorizar";

    /// <summary>Lo que <see cref="Entrada"/> pone: la compra que pide el comprador.</summary>
    public IReadOnlyCollection<string> PoneAlAbrir { get; } = new[] { EventId, Comprador, Lineas, Comision };

    /// <summary>Lo que <see cref="Leer"/> pone. Los apartados no van: el bloque los recorre desde la saga.</summary>
    public IReadOnlyCollection<string> Reconstruye { get; } = new[] { EventId, Comprador, Total, Cobro };

    public IReadOnlyDictionary<string, IReadOnlyCollection<string>> CamposDeItem { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            [Lineas] = new[] { Localidad, Butaca, Cantidad },
        };

    /// <summary>El aforo de cada línea va a <c>Holds</c>; el cobro, a <c>PaymentId</c>.</summary>
    public IReadOnlyDictionary<string, FormaDeReserva> Reservas { get; } =
        new Dictionary<string, FormaDeReserva>(StringComparer.Ordinal)
        {
            [Apartar] = FormaDeReserva.PorItem,
            [Autorizar] = FormaDeReserva.Unica,
        };

    /// <summary>La entrada de la fase que abre la compra.</summary>
    public static FlowContext Entrada(string eventId, Ref comprador, IReadOnlyList<TicketLine> lineas, decimal comision)
        => new FlowContext()
            .Set(EventId, eventId)
            .Set(Comprador, comprador)
            .Set(Lineas, lineas.Select(Item).ToList())
            .Set(Comision, comision);

    /// <summary>Una línea de la compra como ítem del bloque que la recorre.</summary>
    public static FlowContext Item(TicketLine linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        return new FlowContext()
            .Set(Localidad, linea.Tier)
            .Set(Butaca, linea.Seat)
            .Set(Cantidad, linea.Quantity);
    }

    /// <summary>Y de vuelta: el ítem como la línea que el dominio revisa.</summary>
    public static TicketLine Linea(FlowContext item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new TicketLine(item.Get<string>(Localidad), item.Get<string?>(Butaca), item.Get<int>(Cantidad));
    }

    public TicketingSaga Crear(string sagaId, FlowContext ctx, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        return new TicketingSaga(sagaId, ctx.Get<Ref>(Comprador), ctx.Get<string>(EventId), SagaStatus.Running,
            Array.Empty<SeatHold>(), null, ctx.Get<Money>(Total),
            Array.Empty<Compensation>(), null, ahora);
    }

    public FlowContext Leer(TicketingSaga saga)
    {
        ArgumentNullException.ThrowIfNull(saga);
        return new FlowContext()
            .Set(EventId, saga.EventId)
            .Set(Comprador, saga.Buyer)
            .Set(Total, saga.Total)
            .Set(Cobro, saga.PaymentId);
    }

    public TicketingSaga ConReserva(TicketingSaga saga, string paso, HoldLeg reservado, FlowContext? item)
    {
        ArgumentNullException.ThrowIfNull(saga);
        ArgumentNullException.ThrowIfNull(reservado);
        switch (paso)
        {
            case Apartar:
                var linea = Linea(item ?? throw new InvalidOperationException($"«{Apartar}» reserva por línea y llegó sin línea."));
                return saga with
                {
                    Holds = saga.Holds
                        .Append(new SeatHold(reservado.HoldId, reservado.CloseTargetId, linea.Quantity, linea.Tier, linea.Seat))
                        .ToList(),
                };
            case Autorizar:
                return saga with { PaymentId = reservado.HoldId };
            default:
                throw new InvalidOperationException($"La compra de entradas no guarda reservas de «{paso}».");
        }
    }

    public TicketingSaga ConError(TicketingSaga saga, string? falla)
    {
        ArgumentNullException.ThrowIfNull(saga);
        return saga with { LastError = falla };
    }
}
