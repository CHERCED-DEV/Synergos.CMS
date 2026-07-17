namespace Synergos.CMS.Interfaces;

/// <summary>
/// Referencia a un documento adjuntado al expediente: su id + nombre + estado
/// (<c>accepted | rejected</c>) + cuándo se subió. El binario real NO viaja por este
/// seam en la demo (solo la metadata) — el adapter de producción sube a blob storage +
/// escanea. Subir documentos es requisito explícito de las Ventanillas Únicas CO (doc
/// gobierno.md §Fuentes).
/// </summary>
public sealed record CitizenDocumentRef(
    string Id,
    string CaseId,
    string Name,
    string Status,
    DateTimeOffset UploadedAt);

/// <summary>
/// Subida de documentos al expediente del vertical Gobierno (doc gobierno.md §4).
/// Registra la metadata (sin persistencia binaria real en la demo), la adjunta al
/// expediente y deja rastro forense de la subida.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica estado: adjunta sobre el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP). REUSA
/// <see cref="IAuditTrailWriter"/> (ADR 0037): cada subida es un evento append-only
/// <c>gov.document-upload</c>. El binario NO se persiste en la demo (solo metadata);
/// el adapter real sube a blob storage + escaneo antivirus implementando la misma
/// seam. ADR 0075.
/// </remarks>
public interface IDocumentUploadService
{
    /// <summary>
    /// Adjunta un documento con <paramref name="name"/> al expediente
    /// <paramref name="caseId"/>, lo marca <c>accepted</c> y devuelve su referencia.
    /// Lanza <see cref="ArgumentException"/> si el expediente no existe o el nombre
    /// viene vacío.
    /// </summary>
    Task<CitizenDocumentRef> UploadAsync(
        string caseId,
        string name,
        CancellationToken cancellationToken = default);
}
