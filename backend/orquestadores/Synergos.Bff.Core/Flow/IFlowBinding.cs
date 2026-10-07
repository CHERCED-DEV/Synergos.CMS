namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Cómo un dominio guarda en SU saga lo que el flujo declarado va haciendo.
/// </summary>
/// <typeparam name="TSaga">La saga del dominio, con su forma de siempre.</typeparam>
/// <remarks>
/// <para><b>Es la costura mínima, y a propósito no «da forma al record entero».</b> Una función
/// que proyectara la saga desde el contexto sería el flujo imperativo otra vez, escrito del otro
/// lado. Acá el dominio contesta cinco preguntas concretas: con qué nace la saga, qué hace falta
/// para continuarla, y qué campo suyo cambia cuando se aparta algo, cuando se reserva el cargo o
/// cuando hay un error. Las compensaciones y el estado los lleva el intérprete, que es lo que
/// tienen en común los cuatro.</para>
///
/// <para><b>Por qué la saga no se vuelve genérica en F1.</b> Su forma es lo que está en disco y lo
/// que el CMS lee: <c>Holds</c> con su localidad y su butaca, <c>PaymentId</c>, <c>Total</c>.
/// Cambiarla obligaría a migrar las sagas en vuelo; esto las deja byte a byte.</para>
/// </remarks>
public interface IFlowBinding<TSaga> where TSaga : class, ISaga<TSaga>
{
    /// <summary>
    /// La saga recién nacida, <c>Running</c>, con lo que la fase lleva calculado.
    /// </summary>
    /// <remarks>
    /// La llama el intérprete justo antes del primer tramo que reserva algo, no antes: una compra
    /// cuyo precio no se pudo cotizar no deja una saga deshecha de más.
    /// </remarks>
    TSaga Crear(string sagaId, FlowContext ctx, DateTimeOffset ahora);

    /// <summary>Lo que una fase posterior necesita leer, reconstruido desde la saga guardada.</summary>
    FlowContext Leer(TSaga saga);

    /// <summary>La saga con un apartado por ítem más; <paramref name="item"/> es el ítem del bloque.</summary>
    TSaga ConApartado(TSaga saga, HoldLeg apartado, FlowContext item);

    /// <summary>La saga con su reserva única (el cobro autorizado).</summary>
    TSaga ConCargo(TSaga saga, string cargo);

    /// <summary>La saga con qué falló, o sin error al completarse.</summary>
    TSaga ConError(TSaga saga, string? falla);
}
