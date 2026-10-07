namespace Synergos.Bff.Core.Flow;

/// <summary>
/// Una reserva que la saga lleva: sobre qué se deshace antes de consumirla y sobre qué después.
/// </summary>
/// <param name="HoldId">Lo reservado en la capacidad: el apartado, la autorización, el pedido.</param>
/// <param name="CloseTargetId">Sobre qué se deshace una vez consumida (el pozo, no el apartado).
/// Igual a <paramref name="HoldId"/> si no cambia.</param>
/// <remarks>
/// Dentro de un bloque <c>para_cada: reservas:&lt;paso&gt;</c> cada una es un ítem con dos campos,
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
/// Lo que cada paso que reserva dejó en la saga, como lo LEE el intérprete: por el identificador
/// del paso en la definición.
/// </summary>
/// <remarks>
/// <para><b>Nombradas por el paso, y no una ranura por forma.</b> Una ranura para «la» reserva
/// única y otra para «las» de cada ítem cabía en Eventos —un cobro y un apartado por línea— y en
/// ningún otro: Salud aparta la hora en Booking Y autoriza el cobro, y Tienda abre el pedido Y
/// autoriza el cobro. Dos reservas únicas en el mismo flujo, y cada una se cierra y se deshace por
/// su lado.</para>
///
/// <para><b>Una ranura de lectura y no una propiedad pública</b>, y la diferencia es el formato en
/// disco: la saga del dominio la implementa EXPLÍCITAMENTE sobre los campos que ya tenía, y
/// <c>System.Text.Json</c> no serializa una implementación explícita. Un diccionario público nuevo
/// saldría en el fichero de cada saga, y un binario anterior ya no podría leer las que estén en
/// vuelo.</para>
///
/// <para><b>Escribir no es cosa de esta interfaz</b>: lo que una reserva guarda además de sus dos
/// identificadores —la cantidad, la localidad, la butaca— es del dominio, y lo arma
/// <see cref="IFlowBinding{TSaga}.ConReserva"/>.</para>
/// </remarks>
public interface IHoldLedger
{
    /// <summary>Lo que reservó el paso <paramref name="paso"/>, en el orden en que lo hizo.</summary>
    /// <returns>Una reserva única da ninguna —si nunca llegó a reservarse— o una; la de un paso que
    /// se repite da una por ítem.</returns>
    /// <exception cref="InvalidOperationException">Si la saga no guarda reservas de ese paso: una
    /// lista vacía diría «nunca se reservó» y el cierre se saltaría en silencio.</exception>
    IReadOnlyList<HoldLeg> Legs(string paso);
}
