using Synergos.Core;

namespace Synergos.Api.Booking.Domain;

/// <summary>Una franja de apertura recurrente, en la hora local del recurso.</summary>
/// <param name="Day">Día de la semana.</param>
/// <param name="From">Desde, inclusive.</param>
/// <param name="To">Hasta, exclusive.</param>
public sealed record OpeningRule(DayOfWeek Day, TimeOnly From, TimeOnly To);

/// <summary>Cuánta antelación exige una cancelación sin penalidad.</summary>
/// <param name="MinNoticeBeforeStart">
/// Antelación mínima respecto del inicio de la reserva. <see cref="TimeSpan.Zero"/> = se puede
/// cancelar hasta el último instante.
/// </param>
/// <remarks>
/// <b>Deliberadamente no sabe de dinero.</b> Booking dice si la cancelación está en plazo; qué
/// se devuelve y a quién lo decide `Api.Payments` y lo ordena el BFF. Meter el monto acá ataría
/// la capacidad a una política comercial que cambia por negocio — y ahí dejaría de servirle al
/// siguiente.
/// </remarks>
public sealed record CancellationPolicy(TimeSpan MinNoticeBeforeStart)
{
    /// <summary>Sin restricción de plazo.</summary>
    public static readonly CancellationPolicy Anytime = new(TimeSpan.Zero);
}

/// <summary>
/// Algo que se puede reservar durante una ventana de tiempo.
/// </summary>
/// <param name="Id">Identificador dentro de esta capacidad.</param>
/// <param name="Subject">
/// A qué apunta, en el vocabulario de quien lo registró — <c>Ref("salud.profesional","dr-1")</c>,
/// <c>Ref("travel.habitacion","101")</c>. <b>Booking no lo interpreta jamás.</b>
/// </param>
/// <param name="Capacity">Cuántas reservas simultáneas admite. 1 para un consultorio; 40 para un aula.</param>
/// <param name="TimeZoneId">Huso en el que se leen las <see cref="Opening"/>.</param>
/// <param name="Opening">
/// Horario de apertura. <b>Vacío significa siempre abierto</b> — ver las notas.
/// </param>
/// <param name="Policy">Política de cancelación.</param>
/// <remarks>
/// <para><b>Por qué "vacío = siempre abierto" y no "vacío = cerrado".</b> Es lo que permite que
/// la misma capacidad sirva a una agenda clínica y a una habitación de hotel. Una consulta
/// médica cae dentro de un horario de atención; una noche de hotel va de las 15:00 de un día a
/// las 11:00 del siguiente — cruza la medianoche y no encaja en ninguna franja diaria. Forzar
/// horarios a todo recurso habría hecho que Booking sirviera a Salud y no a Travel, que es
/// exactamente el fallo que esta capacidad existe para evitar.</para>
///
/// <para><b>Y la limitación, dicha:</b> si un recurso SÍ declara horarios, una ventana que cruza
/// la medianoche se rechaza. Un recurso de 24 h se modela con <see cref="Opening"/> vacío, no
/// con una franja de 00:00 a 23:59.</para>
/// </remarks>
public sealed record Resource(
    string Id,
    Ref Subject,
    int Capacity,
    string TimeZoneId,
    IReadOnlyList<OpeningRule> Opening,
    CancellationPolicy Policy);
