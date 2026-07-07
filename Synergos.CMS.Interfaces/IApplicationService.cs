namespace Synergos.CMS.Interfaces;

/// <summary>
/// Identidad del ciudadano que radica un trámite (once-only: la UI prellena lo que el
/// Estado ya conoce). Es el "solicitante" del expediente.
/// </summary>
public sealed record GovCitizen(
    string Name,
    string Email,
    string? DocumentId = null,
    string? Phone = null);

/// <summary>
/// Resultado de radicar una solicitud: el expediente recién creado
/// (<see cref="Case"/>, en estado <c>Radicado</c> con su número de radicado y primer
/// hito del timeline) + la <see cref="PaymentSessionId"/> si el trámite cobra tasa (el
/// motor abrió una sesión de pago vía <see cref="IPaymentProvider"/>). Trámite gratuito
/// → <see cref="PaymentSessionId"/> null (paso de pago OFF, igual que la visita de
/// Propiedades).
/// </summary>
public sealed record RadicarResult(
    CaseDetail Case,
    string? PaymentSessionId = null);

/// <summary>
/// Agregado raíz del expediente — radica una solicitud del vertical Gobierno (doc
/// gobierno.md §4). Es la pieza del MOTOR que transforma "formulario lleno" en un
/// <strong>expediente de larga vida con número de radicado</strong>: valida el trámite
/// contra el catálogo, calcula la tasa (<see cref="IGovFeeCalculator"/>), abre la sesión
/// de pago si aplica (<see cref="IPaymentProvider"/>), y asienta el primer estado del
/// expediente (radicado) auditándolo vía <see cref="IAuditTrailWriter"/>.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA el motor (no lo reinventa): la tasa se cobra
/// con <see cref="IPaymentProvider"/> (opcional — trámite gratuito apaga el paso) y
/// cada transición de estado del expediente es un evento append-only en
/// <see cref="IAuditTrailWriter"/> (ADR 0037 — el diferenciador del dominio). El estado
/// (expedientes radicados) es compartido con <see cref="ICaseTrackingProvider"/> y
/// <see cref="ICaseWorkflowService"/> vía composición (DIP), igual que
/// Eventos. <see cref="RadicarAsync"/> NO es idempotente sobre cada llamada (cada
/// radicación genera un expediente nuevo, como una orden); la idempotencia vive en las
/// transiciones de estado posteriores. ADR 0075.
/// </remarks>
public interface IApplicationService
{
    /// <summary>
    /// Radica una solicitud: crea el expediente a partir del trámite + las respuestas
    /// del formulario + el ciudadano, calcula la tasa y abre la sesión de pago si
    /// aplica, y deja el expediente en estado <c>radicado</c>. Lanza
    /// <see cref="ArgumentException"/> si el trámite no existe o faltan campos
    /// requeridos del formulario.
    /// </summary>
    Task<RadicarResult> RadicarAsync(
        string tramiteId,
        IReadOnlyDictionary<string, string> answers,
        GovCitizen citizen,
        CancellationToken cancellationToken = default);
}
