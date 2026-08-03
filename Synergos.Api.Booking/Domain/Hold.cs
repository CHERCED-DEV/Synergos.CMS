using Synergos.Core;

namespace Synergos.Api.Booking.Domain;

/// <summary>
/// Un bloqueo temporal sobre una ventana de un recurso.
/// </summary>
/// <param name="Id">Identificador del hold.</param>
/// <param name="ResourceId">Sobre qué recurso.</param>
/// <param name="Window">Qué ventana bloquea.</param>
/// <param name="HeldFor">Para quién — opaco.</param>
/// <param name="CreatedAt">Cuándo se tomó.</param>
/// <param name="ExpiresAt">Cuándo deja de valer, aunque nadie lo suelte.</param>
/// <param name="Released">Si se soltó explícitamente.</param>
/// <param name="ConfirmedReservationId">La reserva que salió de él, si se confirmó.</param>
/// <remarks>
/// <para><b>Por qué existe el hold y no se confirma directo.</b> Es el paso barato y reversible
/// de cualquier flujo que cruza capacidades. Reservar una cita con copago toca Booking <i>y</i>
/// Payments: si se cobrara primero y el cupo se hubiera ido, habría que devolver plata; con el
/// hold se toma el cupo —gratis—, se cobra, y recién ahí se confirma. Si el cobro falla, se
/// suelta y no pasó nada.</para>
///
/// <para><b>Por qué expira solo.</b> Un cliente que se cae a mitad del flujo no vuelve a soltar
/// nada. Sin vencimiento, cada intento abandonado se queda con un cupo para siempre y el recurso
/// se agota sin que nadie haya reservado — el peor modo de fallo posible para una agenda.</para>
/// </remarks>
public sealed record Hold(
    string Id,
    string ResourceId,
    TimeWindow Window,
    Ref HeldFor,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    bool Released = false,
    string? ConfirmedReservationId = null)
{
    /// <summary>Si sigue ocupando cupo en el instante dado.</summary>
    /// <remarks>
    /// Un hold ya confirmado <b>deja de contar como hold</b>: su cupo lo ocupa ahora la reserva.
    /// Contar los dos haría que cada confirmación consumiera dos plazas de un recurso — y en un
    /// consultorio de capacidad 1, que la primera cita bloqueara el día entero.
    /// </remarks>
    public bool IsActive(DateTimeOffset now)
        => !Released && ConfirmedReservationId is null && now < ExpiresAt;
}
