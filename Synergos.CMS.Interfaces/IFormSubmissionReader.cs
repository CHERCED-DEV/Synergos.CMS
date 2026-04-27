namespace Synergos.CMS.Interfaces;

/// <summary>
/// Seam read-only para listar submissions persistidas — opcional,
/// implementada solo por adapters que soportan inspection (filesystem,
/// SQL). Adapters fire-and-forget (queue, webhook directo, email-only)
/// no la implementan; el dashboard <c>/admin/forms</c> simplemente
/// muestra empty si no hay reader registrado o devuelve 0 items.
/// </summary>
/// <remarks>
/// Separada de <see cref="IFormSubmissionHandler"/> porque el write
/// path (despacho) y el read path (admin inspection) tienen
/// requisitos distintos. Un handler que enqueue a Service Bus no
/// puede listar sin reentrar al broker.
///
/// La impl por defecto vive en
/// <c>Synergos.CMS.Web.Services.FileSystemFormSubmissionHandler</c>
/// (que implementa AMBAS interfaces). Composer registra el mismo
/// instance bajo los 2 contratos.
/// </remarks>
public interface IFormSubmissionReader
{
    /// <summary>
    /// Devuelve una página de submissions persistidas, ordenadas por
    /// <see cref="FormSubmissionListItem.ReceivedAtUtc"/> descendente
    /// (más reciente primero). <paramref name="formKeyFilter"/>
    /// opcional limita a un form específico.
    /// <paramref name="fromUtc"/> y <paramref name="toUtc"/> opcionales
    /// limitan por ventana temporal (ambos inclusive — útil para CSV
    /// export con date range).
    /// </summary>
    FormSubmissionsPage GetRecent(
        int page,
        int pageSize,
        string? formKeyFilter = null,
        DateTime? fromUtc = null,
        DateTime? toUtc = null);

    /// <summary>
    /// Lista los form keys que tienen submissions persistidas. Útil
    /// para popular el dropdown de filter en el dashboard.
    /// </summary>
    IReadOnlyList<string> ListFormKeys();

    /// <summary>
    /// Devuelve el detalle completo de una submission identificada por
    /// (<paramref name="formKey"/>, <paramref name="storageId"/>). El
    /// <c>storageId</c> es opaco para el caller — el adapter lo
    /// interpreta (el FileSystem usa el filename sin extensión).
    /// Devuelve null si no existe.
    /// </summary>
    FormSubmissionDetail? GetSubmission(string formKey, string storageId);

    /// <summary>
    /// Elimina una submission persistida (acción destructiva, idempotente).
    /// Devuelve true si se eliminó algo, false si no existía.
    /// Para adapters read-only sin capacidad de delete, retornar
    /// false sin excepción.
    /// </summary>
    Task<bool> DeleteAsync(string formKey, string storageId, CancellationToken cancellationToken);
}

/// <summary>
/// Submission completa con todos los fields, retornada por
/// <see cref="IFormSubmissionReader.GetSubmission"/>.
/// </summary>
public sealed record FormSubmissionDetail(
    string FormKey,
    string StorageId,
    DateTime ReceivedAtUtc,
    string? ClientIp,
    string? UserAgent,
    string? Referrer,
    IReadOnlyDictionary<string, string> Fields);

/// <summary>
/// Snapshot de una submission para listing — sin los fields
/// completos (que se cargan on-demand al abrir un detalle).
/// </summary>
/// <param name="FormKey">Key del form al que pertenece la submission
///   (mismo valor que el form definido en backoffice).</param>
/// <param name="StorageId">Identificador URL-safe relativo al
///   adapter (FileSystem usa filename sin extensión). Usado como
///   key para <see cref="IFormSubmissionReader.GetSubmission"/>.</param>
/// <param name="ReceivedAtUtc">Timestamp UTC en que el adapter
///   persistió la submission.</param>
/// <param name="ClientIp">IP remota del visitante si fue capturada;
///   null cuando el adapter no la persistió o el header faltó.</param>
/// <param name="FieldCount">Cantidad de fields que tenía la
///   submission — útil para mostrar en el listing sin cargar el body.</param>
/// <param name="StorageReference">Path/ID absoluto interno del
///   adapter — solo para logs/debugging, no exponer al UI.</param>
public sealed record FormSubmissionListItem(
    string FormKey,
    string StorageId,
    DateTime ReceivedAtUtc,
    string? ClientIp,
    int FieldCount,
    string StorageReference);

/// <summary>
/// Página paginada de submissions.
/// </summary>
public sealed record FormSubmissionsPage(
    IReadOnlyList<FormSubmissionListItem> Items,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (TotalCount + PageSize - 1) / PageSize;
    public bool HasNext => Page < TotalPages;
    public bool HasPrev => Page > 1;
}
