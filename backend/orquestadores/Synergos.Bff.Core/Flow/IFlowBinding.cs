namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Cómo un dominio guarda en SU saga lo que el flujo declarado va haciendo.
/// </summary>
/// <typeparam name="TSaga">La saga del dominio, con su forma de siempre.</typeparam>
/// <remarks>
/// <para><b>Es la costura mínima, y a propósito no «da forma al record entero».</b> Una función
/// que proyectara la saga desde el contexto sería el flujo imperativo otra vez, escrito del otro
/// lado. Acá el dominio contesta cuatro preguntas concretas: con qué nace la saga, qué hace falta
/// para continuarla, qué campo suyo cambia cuando un paso reserva algo y cuál cuando hay un
/// error. Las compensaciones y el estado los lleva el intérprete, que es lo que tienen en común
/// todos los flujos.</para>
///
/// <para><b>Y declara lo que su código pone en el contexto</b> —qué pone al abrir, qué reconstruye, qué campos trae
/// cada ítem, qué reservas sabe guardar—, porque son los nombres que la definición lee y el
/// validador no tiene otra forma de saberlos. Sin la declaración, una errata en el JSON pasa el
/// arranque y lanza en la primera compra; con ella, no arranca.</para>
///
/// <para><b>Por qué la saga no se vuelve genérica.</b> Su forma es lo que está en disco y lo que el
/// CMS lee: <c>Holds</c> con su localidad y su butaca, <c>PaymentId</c>, <c>Total</c>. Cambiarla
/// obligaría a migrar las sagas en vuelo; esto las deja byte a byte.</para>
/// </remarks>
public interface IFlowBinding<TSaga> where TSaga : class, ISaga<TSaga>
{
    /// <summary>
    /// Los nombres que la fachada pone en el contexto de la fase que ABRE: lo que la definición
    /// puede declarar como <c>entrada</c>.
    /// </summary>
    /// <remarks>
    /// Sin esto, renombrar «comprador» en el código —o en el JSON— arrancaba igual y lanzaba al
    /// autorizar, con el aforo ya apartado. El validador exige que toda la <c>entrada</c> de la
    /// definición esté acá; el intérprete, que la fachada de verdad la ponga antes del primer paso.
    /// </remarks>
    IReadOnlyCollection<string> PoneAlAbrir { get; }

    /// <summary>
    /// Los nombres que <see cref="Leer"/> pone en el contexto: lo ÚNICO que una fase que continúa
    /// tiene antes de su primer paso.
    /// </summary>
    /// <remarks>
    /// El contexto de la fase anterior no se guardó —es transitorio a propósito—, así que leer en
    /// <c>cerrar</c> algo que sólo escribió <c>abrir</c> lanzaría después de capturar. El validador
    /// arranca cada fase que continúa con estos nombres y nada más.
    /// </remarks>
    IReadOnlyCollection<string> Reconstruye { get; }

    /// <summary>
    /// Por cada lista de ítems que el dominio pone en el contexto —en la entrada o al
    /// reconstruir—, los campos que trae cada ítem.
    /// </summary>
    /// <remarks>
    /// Son los que un bloque <c>para_cada</c> puede leer sin que nadie los escriba antes:
    /// <c>linea.cantidad</c> sí, <c>linea.cantida</c> no arranca.
    /// </remarks>
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> CamposDeItem { get; }

    /// <summary>Los pasos cuya reserva sabe guardar <see cref="ConReserva"/>, y de qué forma.</summary>
    IReadOnlyDictionary<string, FormaDeReserva> Reservas { get; }

    /// <summary>
    /// La saga recién nacida, <c>Running</c>, con lo que la fase lleva calculado.
    /// </summary>
    /// <remarks>
    /// La llama el intérprete justo antes del primer tramo que reserva algo, no antes: una compra
    /// cuyo precio no se pudo cotizar no deja una saga deshecha de más.
    /// </remarks>
    TSaga Crear(string sagaId, FlowContext ctx, DateTimeOffset ahora);

    /// <summary>Lo que una fase posterior necesita leer, reconstruido desde la saga guardada.</summary>
    /// <remarks>Tiene que poner, como mínimo, todo lo de <see cref="Reconstruye"/>.</remarks>
    FlowContext Leer(TSaga saga);

    /// <summary>La saga con una reserva más del paso <paramref name="paso"/>.</summary>
    /// <param name="saga">La saga.</param>
    /// <param name="paso">El paso que reservó: una de las claves de <see cref="Reservas"/>.</param>
    /// <param name="reservado">Lo reservado y sobre qué se deshace una vez consumido.</param>
    /// <param name="item">El ítem del bloque si la reserva es por ítem; <c>null</c> si es única.</param>
    TSaga ConReserva(TSaga saga, string paso, HoldLeg reservado, FlowContext? item);

    /// <summary>La saga con qué falló, o sin error al completarse.</summary>
    TSaga ConError(TSaga saga, string? falla);
}

/// <summary>Si un paso reserva una vez por saga o una vez por ítem.</summary>
public enum FormaDeReserva
{
    /// <summary>Fuera de un bloque: una por saga (el cobro, la hora en la agenda, el pedido).</summary>
    Unica,

    /// <summary>Dentro de un bloque <c>para_cada</c>: una por ítem (el aforo de cada línea).</summary>
    PorItem,
}

/// <summary>
/// Lo que el CÓDIGO de un orquestador da por hecho de su definición: se cruza con ella al arrancar.
/// </summary>
/// <param name="Fases">Las fases que la fachada invoca, en el orden de la definición: la primera abre.</param>
/// <param name="PoneAlAbrir">Lo que la fachada pone en la fase que abre.</param>
/// <param name="Reconstruye">Lo que <see cref="IFlowBinding{TSaga}.Leer"/> pone al continuar.</param>
/// <param name="CamposDeItem">Los campos de cada lista de ítems que el dominio pone en el contexto.</param>
/// <param name="Reservas">Las reservas que la saga sabe guardar, por paso.</param>
/// <param name="Saga">El tipo de saga: tiene que tener la ranura que el intérprete lee.</param>
/// <remarks>
/// <b>Existe porque el JSON y el C# se escriben en sitios distintos.</b> La definición nombra fases,
/// campos y pasos que algún código tiene que poner; si los dos se desvían, el lector y el
/// validador de la definición sola no lo ven y la primera compra sí.
/// </remarks>
public sealed record ContratoDelFlujo(
    IReadOnlyList<string> Fases,
    IReadOnlyCollection<string> PoneAlAbrir,
    IReadOnlyCollection<string> Reconstruye,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> CamposDeItem,
    IReadOnlyDictionary<string, FormaDeReserva> Reservas,
    Type Saga)
{
    /// <summary>El contrato de un binding y las fases que su fachada invoca.</summary>
    public static ContratoDelFlujo De<TSaga>(IFlowBinding<TSaga> binding, IReadOnlyList<string> fases)
        where TSaga : class, ISaga<TSaga>
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(fases);
        return new(fases, binding.PoneAlAbrir, binding.Reconstruye, binding.CamposDeItem, binding.Reservas, typeof(TSaga));
    }
}
