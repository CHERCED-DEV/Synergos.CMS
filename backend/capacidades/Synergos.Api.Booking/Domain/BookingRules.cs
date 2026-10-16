using Synergos.Core;

namespace Synergos.Api.Booking.Domain;

/// <summary>
/// Las reglas que Booking puede aplicar <b>sola</b>, sin preguntarle nada a nadie.
/// </summary>
/// <remarks>
/// <para>Esta clase es la respuesta al filtro del doc 07 §1: <i>una capacidad tiene que poder
/// decir NO por sí sola</i>. Si esto estuviera vacío, Booking sería una base de datos con HTTP.</para>
///
/// <para><b>Todo puro y con el reloj por parámetro.</b> Nada de <c>DateTimeOffset.UtcNow</c> acá
/// dentro: la mitad de los errores de una agenda son de borde temporal —el hold que vence
/// justo, la cancelación en el límite del plazo— y sin reloj inyectable esos casos no se
/// reproducen, solo se sufren.</para>
///
/// <para><b>Devuelven <c>Rejection?</c> y no <c>Result&lt;T&gt;</c>:</b> son guardas, no
/// productoras de valor. <c>null</c> = adelante. El <c>Result&lt;T&gt;</c> aparece en el
/// servicio, que sí devuelve algo.</para>
/// </remarks>
public static class BookingRules
{
    /// <summary>Prefijo de todos los códigos de rechazo de esta capacidad.</summary>
    public const string CodePrefix = "booking";

    /// <summary>
    /// Si la ventana pedida cabe en el horario de apertura del recurso.
    /// </summary>
    /// <remarks>
    /// <b>Sin horarios declarados, todo cabe</b> — es lo que permite que la misma capacidad
    /// sirva a una agenda clínica (con horario) y a una habitación de hotel (una noche cruza la
    /// medianoche y no encaja en ninguna franja diaria). Ver las notas de <see cref="Resource"/>.
    /// </remarks>
    public static Rejection? CheckOpeningHours(Resource resource, TimeWindow window)
    {
        if (resource.Opening.Count == 0) return null;
        if (Zone(resource, out var zone) is { } malHuso) return malHuso;

        var localStart = TimeZoneInfo.ConvertTime(window.Start, zone);
        var localEnd = TimeZoneInfo.ConvertTime(window.End, zone);

        if (localStart.Date != localEnd.Date)
        {
            return Rejection.Conflict($"{CodePrefix}.crosses_midnight",
                "Un recurso con horario declarado no admite ventanas que cruzan la medianoche. " +
                "Un recurso de 24 h se modela sin horario, no con una franja de 00:00 a 23:59.");
        }

        var from = TimeOnly.FromDateTime(localStart.DateTime);
        var to = TimeOnly.FromDateTime(localEnd.DateTime);

        var cabe = resource.Opening.Any(r =>
            r.Day == localStart.DayOfWeek && from >= r.From && to <= r.To);

        return cabe
            ? null
            : Rejection.Conflict($"{CodePrefix}.outside_opening_hours",
                $"El recurso no atiende {localStart.DayOfWeek} de {from} a {to}.");
    }

    /// <summary>
    /// Si la ventana es una franja del recurso: de su duración y, si tiene horario, empezando en la grilla
    /// de la franja de apertura que la contiene (núcleo, NB-1 de 011).
    /// </summary>
    /// <remarks>
    /// <para><b>Sin franja declarada, todo vale</b>, como siempre: la ventana la decide quien reserva. Es lo
    /// que deja a una noche de hotel seguir siendo una noche.</para>
    ///
    /// <para><b>El largo, porque el precio es por servicio.</b> La ventana llega del navegador: sin esta
    /// regla, una franja de tres horas se cobra como una de treinta minutos.</para>
    ///
    /// <para><b>La grilla, porque las franjas libres son ésas.</b> Una cita a las 10:15 en una agenda de
    /// treinta minutos que abre a las 08:00 parte en dos las franjas de las 10:00 y de las 10:30: las dos
    /// quedan medio ocupadas y ninguna se puede ofrecer entera. La grilla se cuenta desde el inicio de la
    /// franja de apertura, en el huso del recurso. Sin horario no hay desde dónde contarla, y sólo se mira
    /// el largo. Una ventana fuera de horario no es asunto de esta regla: la rechaza
    /// <see cref="CheckOpeningHours"/>.</para>
    ///
    /// <para><b>Inválido y no conflicto</b>, como la ventana en el pasado: la franja la dice el recurso y
    /// quien reserva la calculó mal. «Arreglá lo que mandaste», no «no hay sitio».</para>
    /// </remarks>
    public static Rejection? CheckSlot(Resource resource, TimeWindow window)
    {
        if (resource.SlotMinutes is not { } minutos) return null;

        var largo = window.End - window.Start;
        if (largo != TimeSpan.FromMinutes(minutos))
        {
            return Rejection.Invalid($"{CodePrefix}.bad_slot_length",
                $"El recurso se reserva por franjas de {minutos} minutos y la ventana dura {largo.TotalMinutes:0.##}.");
        }

        if (resource.Opening.Count == 0) return null;
        if (Zone(resource, out var zone) is { } malHuso) return malHuso;

        var localStart = TimeZoneInfo.ConvertTime(window.Start, zone);
        var desde = TimeOnly.FromDateTime(localStart.DateTime);
        var regla = resource.Opening.FirstOrDefault(r => r.Day == localStart.DayOfWeek && desde >= r.From && desde < r.To);
        if (regla is null) return null;

        var corrimiento = (desde - regla.From).Ticks % TimeSpan.FromMinutes(minutos).Ticks;
        return corrimiento == 0
            ? null
            : Rejection.Invalid($"{CodePrefix}.off_slot_grid",
                $"Las franjas de {minutos} minutos empiezan a las {regla.From:HH:mm} y cada {minutos} minutos; {desde:HH:mm} no es una.");
    }

    /// <summary>El huso del recurso, o el rechazo si no lo conoce este servicio.</summary>
    internal static Rejection? Zone(Resource resource, out TimeZoneInfo zone)
    {
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(resource.TimeZoneId);
            return null;
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Un huso inválido es un recurso mal registrado, no una petición mala. Se rechaza
            // como conflicto de estado para que no se lea como culpa de quien reserva.
            zone = TimeZoneInfo.Utc;
            return Rejection.Conflict($"{CodePrefix}.bad_timezone",
                $"El recurso declara el huso '{resource.TimeZoneId}', que este servicio no conoce.");
        }
    }

    /// <summary>
    /// Si queda cupo para una ventana más.
    /// </summary>
    /// <param name="resource">El recurso, con su capacidad.</param>
    /// <param name="occupied">Las ventanas que YA ocupan cupo — holds activos y reservas vigentes.</param>
    /// <param name="window">La ventana que se quiere tomar.</param>
    /// <remarks>
    /// El conteo es de ventanas <b>solapadas</b>, no de reservas del día: un aula de capacidad 40
    /// puede tener cien reservas en la semana y seguir libre a las 10:00. Que
    /// <see cref="TimeWindow.Overlaps"/> trate las ventanas contiguas como NO solapadas es lo
    /// que hace que 10:00–10:30 y 10:30–11:00 quepan en un recurso de capacidad 1.
    /// </remarks>
    public static Rejection? CheckCapacity(Resource resource, IEnumerable<TimeWindow> occupied, TimeWindow window)
    {
        var solapadas = occupied.Count(w => w.Overlaps(window));

        return solapadas < resource.Capacity
            ? null
            : Rejection.Conflict($"{CodePrefix}.at_capacity",
                $"El recurso admite {resource.Capacity} en paralelo y ya hay {solapadas} en esa ventana.");
    }

    /// <summary>Si la ventana pedida no empieza en el pasado.</summary>
    /// <remarks>
    /// Reservar hacia atrás no es un capricho prohibido: es que la ventana ya no puede ocuparse,
    /// y aceptarlo produce agendas con citas de ayer que nadie puede atender ni cancelar.
    /// </remarks>
    public static Rejection? CheckNotInThePast(TimeWindow window, DateTimeOffset now)
        => window.Start >= now
            ? null
            : Rejection.Invalid($"{CodePrefix}.window_in_the_past",
                $"La ventana empieza en {window.Start:O}, antes de ahora ({now:O}).");

    /// <summary>Si el hold sigue sirviendo para confirmar.</summary>
    public static Rejection? CheckHoldIsUsable(Hold hold, DateTimeOffset now)
    {
        if (hold.ConfirmedReservationId is not null)
        {
            // Conflict y no Invalid: la petición está bien formada, el estado es el que no da.
            return Rejection.Conflict($"{CodePrefix}.hold_already_confirmed",
                $"El hold ya produjo la reserva {hold.ConfirmedReservationId}.");
        }
        if (hold.Released)
        {
            return Rejection.Conflict($"{CodePrefix}.hold_released", "El hold ya se soltó.");
        }
        if (now >= hold.ExpiresAt)
        {
            // Expired y no NotFound: le dice al llamador que rehaga el flujo desde el hold, en
            // vez de mandarlo a buscar un identificador que escribió bien.
            return Rejection.Expired($"{CodePrefix}.hold_expired",
                $"El hold venció en {hold.ExpiresAt:O}.");
        }
        return null;
    }

    /// <summary>Si la reserva se puede cancelar según la política del recurso.</summary>
    public static Rejection? CheckCancellable(Reservation reservation, CancellationPolicy policy, DateTimeOffset now)
    {
        if (reservation.Status == ReservationStatus.Cancelled)
        {
            return Rejection.Conflict($"{CodePrefix}.already_cancelled", "La reserva ya estaba cancelada.");
        }

        var limite = reservation.Window.Start - policy.MinNoticeBeforeStart;

        return now <= limite
            ? null
            : Rejection.Conflict($"{CodePrefix}.cancellation_too_late",
                $"La política exige {policy.MinNoticeBeforeStart} de antelación; el plazo venció en {limite:O}.");
    }
}
