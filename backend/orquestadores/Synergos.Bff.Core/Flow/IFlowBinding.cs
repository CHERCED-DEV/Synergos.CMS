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
    /// Por fase, lo que la fachada pone SÓLO para esa llamada: la <c>efimera</c> que la definición
    /// puede declarar. Por defecto, nada.
    /// </summary>
    /// <remarks>
    /// <para>Es la forma de darle a un paso algo que la saga no puede guardar —la dirección del
    /// aviso: la saga está fijada byte a byte y no lleva datos personales— sin que pase por
    /// <see cref="Crear"/>, <see cref="ConReserva"/> ni <see cref="ConError"/>. Entra con la entrada
    /// de la fase y muere con ella.</para>
    ///
    /// <para>El validador exige que toda la <c>efimera</c> de la definición esté acá y que ningún
    /// paso la lea fuera de su fase; el intérprete, que la fachada la ponga (aunque sea nula) antes
    /// del primer paso. Un binding que no la declara deja la definición sin arrancar, no la ignora.</para>
    /// </remarks>
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> Efimera => ContratoDelFlujo.SinEfimera;

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

    /// <summary>
    /// La saga justo antes de cada escritura, con el contexto de ese instante. Por defecto, la MISMA.
    /// </summary>
    /// <remarks>
    /// <para>Es lo que deja a una saga genérica (núcleo N1) guardar lo que un paso acaba de escribir —el
    /// cobro que se autorizó— en la MISMA escritura que su compensación: si el proceso se cae después,
    /// la fase que cierra tiene de dónde leerlo.</para>
    ///
    /// <para>Una saga tipada no lo necesita: guarda lo suyo en <see cref="Crear"/> y
    /// <see cref="ConReserva"/>. Por eso por defecto devuelve la misma instancia, y el intérprete sólo
    /// escribe de más si el binding devolvió otra: el fichero de una compra de entradas no cambia.</para>
    /// </remarks>
    TSaga AlGuardar(TSaga saga, FlowContext ctx) => saga;

    /// <summary>
    /// Lo que el dominio guarda de cada cierre de una reserva (núcleo N2, NV-3 de Viajes), en la MISMA escritura
    /// que cambia el carácter de su compensación.
    /// </summary>
    /// <param name="saga">La saga, con la compensación de esa reserva ya cambiada.</param>
    /// <param name="reservante">El paso cuya reserva se cerró.</param>
    /// <param name="cerrada">La reserva: lo apartado y el objetivo anotado al reservar.</param>
    /// <param name="objetivo">Lo que produjo el cierre (la reserva confirmada de un apartado), o nulo si no produjo nada.</param>
    /// <remarks>
    /// <para>Una saga tipada que guarda por ítem lo que confirmó (<c>ItemHold.ReservationId</c> de Viajes) lo
    /// necesita: de ahí salen lo confirmado en la respuesta y la cancelación de un viaje confirmado. Sin esto,
    /// la compensación reescrita apunta a la reserva y nadie sabe ya de qué apartado salió.</para>
    ///
    /// <para>Por defecto devuelve la misma saga: el fichero de una saga que no lo necesita no cambia.</para>
    /// </remarks>
    TSaga ConCierre(TSaga saga, string reservante, HoldLeg cerrada, string? objetivo) => saga;

    /// <summary>
    /// Si ESTA saga admite soltar un ítem cuyo cierre falló y seguir con los demás: la confirmación parcial
    /// (núcleo N2, NV-2 de Viajes, 006-P2 forma A). Por defecto, no.
    /// </summary>
    /// <remarks>
    /// La política la guarda el dominio al abrir, desde la configuración del sitio; la definición sólo declara
    /// <c>al_fallar: soltar</c> y no evalúa nada. Sin un sí, el paso aborta como siempre.
    /// </remarks>
    bool SueltaSiFalla(TSaga saga) => false;

    /// <summary>
    /// Lo que el dominio hace con un ítem que se soltó (NV-2): marcarlo no cumplido, sacarlo de su ranura para
    /// que repetir la fase no lo reintente y, si hace falta, armar compensaciones NUEVAS para él (la devolución
    /// de su parte). El intérprete ejecuta en el acto las nuevas; las que fallan quedan pendientes.
    /// </summary>
    /// <param name="saga">La saga, con la compensación de ese ítem ya intentada.</param>
    /// <param name="reservante">El paso cuya reserva se soltó.</param>
    /// <param name="soltada">La reserva del ítem.</param>
    TSaga ConItemSoltado(TSaga saga, string reservante, HoldLeg soltada) => saga;

    /// <summary>
    /// <see cref="SueltaSiFalla(TSaga)"/> con el contexto de la fase (NV-13 de Viajes): lo que pregunta el intérprete. Por
    /// defecto, la respuesta sin contexto de una saga tipada.
    /// </summary>
    /// <remarks>Un binding genérico no guarda la política en un campo de su saga: la tiene en el contexto que la saga
    /// reconstruye, y se la pasa a la extensión del vertical (<see cref="IExtensionDelFlujo"/>).</remarks>
    bool SueltaSiFalla(TSaga saga, FlowContext ctx) => SueltaSiFalla(saga);

    /// <summary>
    /// <see cref="ConItemSoltado(TSaga, string, HoldLeg)"/> con el contexto de la fase (NV-13): lo que llama el intérprete.
    /// Por defecto, el de una saga tipada.
    /// </summary>
    TSaga ConItemSoltado(TSaga saga, string reservante, HoldLeg soltada, FlowContext ctx) => ConItemSoltado(saga, reservante, soltada);

    /// <summary>
    /// Si un vertical extiende este flujo genérico (NV-13), los tipos de compensación que su extensión puede armar al
    /// soltar un ítem; nulo si nadie lo extiende. El validador cruza con esto <c>al_fallar: soltar</c>.
    /// </summary>
    IReadOnlyCollection<string>? ArmaLaExtension => null;

    /// <summary>
    /// Los tipos de valor con que este binding lee y guarda (núcleo N1): sólo un binding genérico los
    /// tiene. Con ellos, el validador cruza además las reglas del modo genérico.
    /// </summary>
    TiposDeValor? Tipos => null;

    /// <summary>
    /// Los tipos de compensación que sabe deshacer el ejecutor de esta saga, si se conocen: el validador
    /// cruza contra ellos el <c>antes</c> y el <c>despues</c> de cada reserva.
    /// </summary>
    IReadOnlyCollection<string>? Deshacer => null;
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
/// <param name="Efimera">Lo que la fachada pone, por fase, sólo para esa llamada.</param>
/// <param name="Tipos">Los tipos de valor de un binding genérico; con ellos se cruzan las reglas del modo
/// genérico (núcleo N1). Nulo en una saga tipada.</param>
/// <param name="Deshacer">Los tipos de compensación que sabe deshacer el ejecutor, si se conocen.</param>
/// <param name="ArmaLaExtension">Lo que puede armar la extensión del vertical al soltar un ítem (NV-13); nulo si nadie
/// extiende el flujo.</param>
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
    Type Saga,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>>? Efimera = null,
    TiposDeValor? Tipos = null,
    IReadOnlyCollection<string>? Deshacer = null,
    IReadOnlyCollection<string>? ArmaLaExtension = null)
{
    /// <summary>Ninguna fase con nada efímero.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyCollection<string>> SinEfimera { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal);

    /// <summary>Lo efímero que la fachada pone en una fase: vacío si no declara nada.</summary>
    public IReadOnlyCollection<string> EfimeraDe(string fase)
        => Efimera?.GetValueOrDefault(fase) ?? Array.Empty<string>();

    /// <summary>El contrato de un binding y las fases que su fachada invoca.</summary>
    public static ContratoDelFlujo De<TSaga>(IFlowBinding<TSaga> binding, IReadOnlyList<string> fases)
        where TSaga : class, ISaga<TSaga>
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(fases);
        return new(fases, binding.PoneAlAbrir, binding.Reconstruye, binding.CamposDeItem, binding.Reservas, typeof(TSaga),
            binding.Efimera, binding.Tipos, binding.Deshacer, binding.ArmaLaExtension);
    }
}
