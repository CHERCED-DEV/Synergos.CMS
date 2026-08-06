namespace Synergos.CMS.Interfaces;

/// <summary>
/// Service que persiste o despacha submissions de formularios custom
/// del CMS (path interno de ADR 0018). Aísla al
/// <c>FormSubmissionsController</c> de la decisión de almacenamiento
/// (filesystem, DB, queue, webhook, email).
/// </summary>
/// <remarks>
/// La implementación por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemFormSubmissionHandler</c> y
/// escribe un JSON por submission a
/// <c>{ContentRoot}/{FormsSettings.StorageRoot}/{formKey}/{timestamp}_{guid}.json</c>.
///
/// Para producción, swappear con un adapter que envíe a una queue
/// (Service Bus, RabbitMQ), un webhook (Slack, custom backend) o un
/// servicio de email (SendGrid). El controller solo conoce esta seam.
/// </remarks>
public interface IFormSubmissionHandler
{
    /// <summary>
    /// Procesa una submission. Llamado por el controller después de
    /// honeypot + rate-limit + field validation.
    /// </summary>
    Task<FormSubmissionResult> SubmitAsync(
        FormSubmissionRequest request,
        CancellationToken cancellationToken);
}

/// <summary>
/// Contexto de un form submission ya validado por el controller.
/// </summary>
/// <param name="FormKey">El <c>formInternalKey</c> del block que generó
///   el form (ej. "contact-form").</param>
/// <param name="Fields">Campos del form ya filtrados (sin honeypot,
///   con valores trimmed, longitud capped). Diccionario insensitive a
///   case del nombre del campo.</param>
/// <param name="ClientIp">IP del cliente — útil para auditoría.</param>
/// <param name="UserAgent">User-Agent header del request, o null.</param>
/// <param name="Referrer">Referer header del request, o null. El
///   controller redirige aquí post-success.</param>
/// <param name="ReceivedAtUtc">Timestamp UTC del request.</param>
public sealed record FormSubmissionRequest(
    string FormKey,
    IReadOnlyDictionary<string, string> Fields,
    string? ClientIp,
    string? UserAgent,
    string? Referrer,
    DateTime ReceivedAtUtc);

/// <summary>
/// Resultado de un submission. <see cref="Success"/> false dispara
/// redirect con <c>?error={ErrorCode}</c> en el controller.
/// </summary>
/// <param name="Success">true cuando el handler aceptó la submission.</param>
/// <param name="ErrorCode">Slug machine-readable del error (ej.
///   "storage-failed", "duplicate"). Null si Success.</param>
/// <param name="StorageReference">Identificador de la submission
///   persistida (path, ID DB, message ID). Útil para logs/debugging.</param>
public sealed record FormSubmissionResult(
    bool Success,
    string? ErrorCode = null,
    string? StorageReference = null)
{
    public static FormSubmissionResult Ok(string storageReference) =>
        new(Success: true, ErrorCode: null, StorageReference: storageReference);

    public static FormSubmissionResult Fail(string errorCode) =>
        new(Success: false, ErrorCode: errorCode, StorageReference: null);
}
