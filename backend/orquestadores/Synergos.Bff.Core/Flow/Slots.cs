namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Un apartado POR ÍTEM que la saga lleva: sobre qué se deshace antes de consumirlo y sobre qué después.
/// </summary>
/// <param name="HoldId">El apartado en la capacidad.</param>
/// <param name="CloseTargetId">Sobre qué se deshace una vez consumido (el pozo, no el apartado).</param>
/// <remarks>
/// Dentro de un bloque <c>para_cada: reservas:&lt;paso&gt;</c> cada uno es un ítem con dos campos,
/// <see cref="CampoHold"/> y <see cref="CampoCierre"/>: es lo que un paso de cierre puede leer.
/// </remarks>
public sealed record HoldLeg(string HoldId, string CloseTargetId)
{
    /// <summary>El campo del ítem con <see cref="HoldId"/>.</summary>
    public const string CampoHold = "holdId";

    /// <summary>El campo del ítem con <see cref="CloseTargetId"/>.</summary>
    public const string CampoCierre = "closeTargetId";
}

/// <summary>
/// Los apartados por ítem de una saga, como los LEE el intérprete.
/// </summary>
/// <remarks>
/// <para><b>Una ranura de lectura y no una propiedad pública</b>, y la diferencia es el formato en
/// disco: la saga del dominio la implementa EXPLÍCITAMENTE, y <c>System.Text.Json</c> no serializa
/// una implementación explícita. Una propiedad pública nueva saldría en el fichero de cada saga, y
/// un binario anterior ya no podría leer las que estén en vuelo.</para>
///
/// <para><b>Escribir no es cosa de esta interfaz</b>: lo que un apartado guarda además de sus dos
/// identificadores —la cantidad, la localidad, la butaca— es del dominio, y lo arma
/// <see cref="IFlowBinding{TSaga}.ConApartado"/>.</para>
/// </remarks>
public interface IHoldLedger
{
    /// <summary>Los apartados, en el orden en que se hicieron.</summary>
    IReadOnlyList<HoldLeg> Legs { get; }
}

/// <summary>
/// La reserva ÚNICA de una saga —la que se declara fuera de un bloque—, como la LEE el intérprete.
/// </summary>
/// <remarks>
/// En los cuatro orquestadores es el cobro autorizado. Se lee para una sola cosa: un paso que la
/// cierra no se ejecuta si la saga no la tiene, porque no hay nada que cerrar — es el
/// <c>if (saga.PaymentId is { } id)</c> que cada <c>ConfirmAsync</c> escribía a mano.
/// </remarks>
public interface IChargeLedger
{
    /// <summary>Lo reservado, o <c>null</c> si nunca llegó a reservarse.</summary>
    string? ChargeRef { get; }
}
