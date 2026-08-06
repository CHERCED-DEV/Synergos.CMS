namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// POCO tipado bindeado de <c>Synergos:Notifications</c> (T4, doc 25). Gobierna las
/// notificaciones de hechos transaccionales (recibo de compra, confirmación de viaje,
/// e-tickets, matrícula, radicado, decisión).
/// </summary>
public sealed class NotificationsSettings
{
    /// <summary>
    /// Interruptor maestro. Default <c>false</c> — opt-in: sin config el sistema se
    /// comporta EXACTO como antes de T4 (nadie notifica). El gate se evalúa ANTES de
    /// reclamar la clave de idempotencia, para no quemarla mientras está apagado.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Tipos apagados individualmente (ej. <c>"gov.case.decided"</c>). Permite silenciar
    /// un hecho sin apagar los demás.
    /// </summary>
    public HashSet<string> DisabledTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Base pública para construir los enlaces del email desde el <c>ActionPath</c>
    /// relativo del evento (ej. <c>https://synergos.local:5001</c>). Vacío = el email no
    /// lleva enlace (nunca un href roto).
    /// </summary>
    public string? PublicBaseUrl { get; init; }

    /// <summary>Reply-To de los correos transaccionales. Vacío = el default del remitente.</summary>
    public string? ReplyTo { get; init; }
}
