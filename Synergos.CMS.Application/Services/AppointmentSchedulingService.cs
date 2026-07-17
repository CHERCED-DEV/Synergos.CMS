using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services;

/// <summary>
/// Lógica PURA de agenda (ADR 0098): decide si una cita nueva entra en conflicto
/// con las citas vigentes de un doctor. Sin IO ni dependencias de Umbraco — el
/// lock y la persistencia viven en la capa Web (<c>LockingAppointmentScheduler</c>).
/// Esto la hace testeable sin infraestructura.
/// </summary>
public sealed class AppointmentSchedulingService
{
    /// <summary>True si los intervalos [aStart,aEnd) y [bStart,bEnd) se solapan.</summary>
    public static bool Overlaps(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
        => aStart < bEnd && bStart < aEnd;

    /// <summary>
    /// True si <paramref name="startUtc"/>–<paramref name="endUtc"/> entra en
    /// conflicto con alguna cita <c>booked</c> existente del doctor: el solape
    /// supera la tolerancia <paramref name="maxOverbookingMinutes"/> (0 = sin
    /// overbooking, cualquier solape es conflicto).
    /// </summary>
    public bool HasConflict(
        IEnumerable<AppointmentSlot> existing, DateTime startUtc, DateTime endUtc, int maxOverbookingMinutes)
    {
        var tolerance = TimeSpan.FromMinutes(Math.Max(0, maxOverbookingMinutes));
        foreach (var a in existing)
        {
            if (!string.Equals(a.Status, "booked", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            // Cantidad de solape = min(fines) - max(inicios). Negativo = sin solape.
            var overlapStart = startUtc > a.StartUtc ? startUtc : a.StartUtc;
            var overlapEnd = endUtc < a.EndUtc ? endUtc : a.EndUtc;
            if (overlapEnd - overlapStart > tolerance)
            {
                return true;
            }
        }
        return false;
    }
}
