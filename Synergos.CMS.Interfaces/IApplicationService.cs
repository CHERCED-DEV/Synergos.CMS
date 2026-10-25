namespace Synergos.CMS.Interfaces;

/// <summary>
/// Identidad del ciudadano que radica un trámite (once-only: la UI prellena lo que el
/// Estado ya conoce). Es el "solicitante" del expediente.
/// </summary>
/// <param name="MemberKey">
/// La identidad SERVER-TRUSTED del solicitante, sellada desde la sesión y <b>nunca</b> del
/// formulario. Es el dueño del expediente. Null = radicado sin sesión (invitado).
/// </param>
/// <remarks>
/// <b>Por qué el <paramref name="MemberKey"/> y no basta el <paramref name="Email"/>:</b> el
/// correo lo teclea quien radica, así que es enumerable y suplantable — cualquiera radica a
/// nombre de otro poniendo su correo, y cualquiera lista los expedientes ajenos sabiéndolo.
/// Es el mismo IDOR que ADR 0103 cerró en Tienda ("identidad server-trusted, no email
/// enumerable"); <c>GovCitizen</c> es el análogo exacto de <c>ShopCustomer</c>.
///
/// <para><b>Y aquí el radicado NO rescata nada</b>, al revés que en Tienda: el
/// <c>orderRef</c> es <c>ord_{guid:N}</c> —inadivinable— y por eso funciona como credencial
/// bearer que sostiene el autoservicio de invitado. El radicado es <c>SG-2026-000001</c>:
/// SECUENCIAL. Se enumera contando, así que no puede ser la llave de nada.</para>
/// </remarks>
public sealed record GovCitizen(
    string Name,
    string Email,
    string? DocumentId = null,
    string? Phone = null,
    Guid? MemberKey = null);

/// <summary>
/// Lo que el ciudadano contestó al radicar sobre la notificación electrónica de los actos de su
/// trámite (009, S-7 y FR-017).
/// </summary>
/// <param name="Accepted">Si la aceptó. No aceptarla no impide radicar.</param>
/// <param name="TextVersion">La versión del texto que leyó (<c>Gov.Tramite.Consent.Version</c>): es lo
/// que prueba QUÉ aceptó.</param>
/// <param name="RespondedAt">Cuándo contestó.</param>
/// <remarks>
/// <b>Sin respuesta registrada es «no consta»</b>, y se representa con <c>null</c>, nunca como una
/// aceptación: así están los expedientes anteriores a 009 y los radicados sin la casilla. Notificar
/// electrónicamente a quien no consta que lo aceptó dejaría corriendo un término sobre un medio que
/// nadie autorizó.
/// </remarks>
public sealed record NotificationConsent(bool Accepted, string TextVersion, DateTimeOffset RespondedAt);

/// <summary>Cómo viaja la aceptación hacia la consola: <c>accepted | declined | unknown</c>.</summary>
public static class NotificationConsentSlugs
{
    public const string Accepted = "accepted";
    public const string Declined = "declined";
    public const string Unknown = "unknown";

    /// <summary>El slug de una respuesta, o <see cref="Unknown"/> si no consta.</summary>
    public static string ToSlug(NotificationConsent? consent)
        => consent is null ? Unknown : consent.Accepted ? Accepted : Declined;
}

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
    /// <param name="consent">Lo que contestó sobre la notificación electrónica (009, FR-017), que se
    /// guarda con las respuestas; <c>null</c> es «no consta». Con default, para que quien radica sin
    /// la casilla siga igual; antes del token, que va último (CA1068).</param>
    Task<RadicarResult> RadicarAsync(
        string tramiteId,
        IReadOnlyDictionary<string, string> answers,
        GovCitizen citizen,
        NotificationConsent? consent = null,
        CancellationToken cancellationToken = default);
}
