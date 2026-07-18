namespace Synergos.CMS.Interfaces;

/// <summary>
/// Referencia a un documento adjuntado al expediente: su id + nombre + estado
/// (<c>accepted | rejected</c>) + cuándo se subió, y —desde T6— el puntero al binario
/// real (<paramref name="FileId"/>) con su content-type y tamaño. Subir documentos es
/// requisito explícito de las Ventanillas Únicas CO (doc gobierno.md §Fuentes).
/// </summary>
/// <param name="FileId">
/// Id OPACO del fichero en <see cref="IPrivateFileStore"/>, o <c>null</c> para los
/// documentos previos a T6 (metadata sin binario). Que sea nullable es lo que permite
/// que los expedientes ya sembrados sigan leyéndose: no tienen bytes detrás y la UI
/// simplemente no les ofrece descarga.
/// </param>
public sealed record CitizenDocumentRef(
    string Id,
    string CaseId,
    string Name,
    string Status,
    DateTimeOffset UploadedAt,
    string? FileId = null,
    string? ContentType = null,
    long SizeBytes = 0);

/// <summary>
/// Subida de documentos al expediente del vertical Gobierno (doc gobierno.md §4).
/// Persiste el binario en el almacén privado, adjunta la metadata al expediente y deja
/// rastro forense de la subida.
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Por eso <b>este seam recibe <c>byte[]</c>, no
/// <c>IFormFile</c></b>: leer el multipart es trabajo del controller (Web), y aquí solo
/// llegan primitivas.</para>
/// <para>NO duplica estado: adjunta sobre el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP). REUSA
/// <see cref="IAuditTrailWriter"/> (ADR 0037): cada subida es un evento append-only
/// <c>gov.document-upload</c>. Los bytes van a <see cref="IPrivateFileStore"/> (cifrado,
/// fuera de wwwroot); el adapter real de producción cambia ese almacén por blob storage
/// + escaneo antivirus implementando la misma seam. ADR 0075.</para>
/// </remarks>
public interface IDocumentUploadService
{
    /// <summary>
    /// Adjunta un documento al expediente <paramref name="caseId"/>: guarda
    /// <paramref name="content"/> en el almacén privado, lo marca <c>accepted</c> y
    /// devuelve su referencia. Lanza <see cref="ArgumentException"/> si el expediente no
    /// existe, el nombre viene vacío o el contenido está vacío.
    /// </summary>
    Task<CitizenDocumentRef> UploadAsync(
        string caseId,
        string name,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default);
}
