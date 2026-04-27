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
    /// </summary>
    FormSubmissionsPage GetRecent(int page, int pageSize, string? formKeyFilter = null);

    /// <summary>
    /// Lista los form keys que tienen submissions persistidas. Útil
    /// para popular el dropdown de filter en el dashboard.
    /// </summary>
    IReadOnlyList<string> ListFormKeys();
}

/// <summary>
/// Snapshot de una submission para listing — sin los fields
/// completos (que se cargan on-demand al abrir un detalle). Mantiene
/// la lista del dashboard ligera incluso con cientos de submissions.
/// </summary>
public sealed record FormSubmissionListItem(
    string FormKey,
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
