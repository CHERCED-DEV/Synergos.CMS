namespace Synergos.CMS.Interfaces;

/// <summary>Una línea del detalle de la notificación (ítem comprado, ticket, etc.).</summary>
public sealed record NotificationLine(
    string Label,
    int Quantity,
    decimal Amount,
    string Currency,
    string? Detail = null);

/// <summary>
/// Los hechos de negocio que hoy notifican (T4, doc 25). Strings ABIERTOS: el dispatcher
/// no conoce ningún dominio, así que un vertical nuevo notifica sin tocar el seam
/// (mismo criterio que <c>INotificationFeed</c> — DIP, ADR 0002).
/// </summary>
public static class NotificationTypes
{
    public const string ShopOrderPaid = "shop.order.paid";
    public const string TravelOrderConfirmed = "travel.order.confirmed";
    public const string EventTicketsConfirmed = "events.tickets.confirmed";
    public const string EnrollmentActive = "academy.enrollment.active";
    public const string GovCaseFiled = "gov.case.filed";
    public const string GovCaseDecided = "gov.case.decided";
}

/// <summary>
/// Un hecho de negocio transaccional que merece avisarle al SUJETO (comprador, viajero,
/// asistente, alumno, ciudadano). Genérico por diseño — un solo evento polimórfico para
/// los 6 hechos, en vez de un record y un notifier por dominio (regla de oro del doc 25:
/// ninguna capacidad transversal se implementa dos veces).
/// </summary>
/// <remarks>
/// <see cref="ResolvedDedupeKey"/> identifica el HECHO DE NEGOCIO, nunca la entrega del
/// transporte. Es lo que impide el doble recibo: <c>ConfirmAsync</c> es idempotente y se
/// ejecuta MÁS DE UNA VEZ por diseño (el cliente vuelve del redirect y llama /confirm
/// mientras el PSP postea el webhook — ambos confirman la misma orden).
/// </remarks>
public sealed record NotificationEvent(
    string Type,
    string SubjectId,
    string ToEmail,
    string ToName,
    string Code,
    DateTimeOffset OccurredAt,
    decimal? Amount = null,
    string? Currency = null,
    IReadOnlyList<NotificationLine>? Lines = null,
    IReadOnlyDictionary<string, string>? Data = null,
    string? ActionPath = null,
    string? DedupeKey = null)
{
    /// <summary>
    /// La clave del hecho. Default <c>{Type}:{SubjectId}</c>; se sobreescribe con
    /// <see cref="DedupeKey"/> cuando el SubjectId NO identifica el hecho (ej. una reserva
    /// que pasa de Partial a Confirmed son DOS hechos; una matrícula cuyo id es un Guid
    /// fresco por llamada no identifica "este alumno en este curso").
    /// </summary>
    public string ResolvedDedupeKey =>
        string.IsNullOrWhiteSpace(DedupeKey) ? $"{Type}:{SubjectId}" : DedupeKey!;
}

/// <summary>
/// Despacha notificaciones de hechos transaccionales. UN seam transversal para los 6
/// hechos y los 5 verticales — el dispatcher no conoce ningún dominio.
/// </summary>
/// <remarks>
/// <b>CONTRATO: nunca lanza.</b> Un email que falla JAMÁS puede tumbar una orden ya
/// pagada. Aun así el llamador envuelve (<c>NotificationEmission.SafeDispatchAsync</c>):
/// la garantía debe ser estructural, no depender de la disciplina de la impl.
/// </remarks>
public interface ITransactionalNotifier
{
    Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default);
}

/// <summary>
/// Marker de canal de entrega. Se registran N (<c>AddSingleton&lt;ITransactionalNotifierChannel, X&gt;()</c>)
/// y el composite abanica. Calca el patrón ya vivo de <see cref="IAlertNotifier"/>/
/// <c>IAlertNotifierChannel</c>. Hoy: email. SMS/push = un AddSingleton más, sin tocar
/// motores ni dispatcher.
/// </summary>
public interface ITransactionalNotifierChannel : ITransactionalNotifier
{
}
