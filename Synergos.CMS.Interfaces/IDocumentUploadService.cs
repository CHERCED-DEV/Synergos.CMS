namespace Synergos.CMS.Interfaces;

/// <summary>
/// Metadata de un documento a subir al expediente (la UI envía nombre + tipo MIME +
/// peso; el binario real NO viaja por este seam en la demo — el adapter de producción
/// sube a blob storage + escanea). Subir PDF es requisito explícito de las Ventanillas
/// Únicas CO (doc gobierno-app-spec §Fuentes).
/// </summary>
public sealed record CitizenDocumentUpload(
    string FileName,
    string ContentType,
    long SizeBytes);

/// <summary>
/// Referencia segura a un documento ya adjuntado al expediente: su id + metadata +
/// estado de validación (<c>accepted | rejected</c>) + url segura (stub local). El
/// binario no se persiste en la demo; solo la metadata.
/// </summary>
public sealed record CitizenDocumentRef(
    string Id,
    string CaseId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string ValidationStatus,
    string SecureUrl);

/// <summary>
/// Resultado de subir documentos a un expediente: una referencia por archivo, con su
/// <see cref="CitizenDocumentRef.ValidationStatus"/> (<c>accepted | rejected</c>).
/// Solo los aceptados quedan adjuntos al expediente.
/// </summary>
public sealed record DocumentUploadResult(IReadOnlyList<CitizenDocumentRef> Uploaded);

/// <summary>
/// Subida de documentos al expediente del vertical Gobierno (doc gobierno-app-spec §4).
/// Valida tipo/peso, registra la metadata (sin persistencia binaria real en la demo) y
/// la adjunta al expediente. Cada subida deja rastro forense.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). REUSA <see cref="IAuditTrailWriter"/> (ADR 0037):
/// cada subida es un evento append-only <c>gov.document-upload</c>. La validación
/// (tipo PDF/imagen, peso máximo) es lógica pura/determinista. El adapter real persiste
/// el binario en blob storage + escaneo antivirus implementando la misma seam. ADR 0075.
/// </remarks>
public interface IDocumentUploadService
{
    /// <summary>
    /// Sube los documentos al expediente <paramref name="caseId"/>: valida cada archivo
    /// (tipo y peso), crea su <see cref="CitizenDocumentRef"/> y lo adjunta. Lanza
    /// <see cref="ArgumentException"/> si el expediente no existe o la lista es vacía.
    /// </summary>
    Task<DocumentUploadResult> UploadAsync(
        string caseId,
        IReadOnlyList<CitizenDocumentUpload> files,
        CancellationToken cancellationToken = default);
}
