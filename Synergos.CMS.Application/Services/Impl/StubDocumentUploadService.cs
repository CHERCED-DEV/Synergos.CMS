using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IDocumentUploadService"/> — subida de documentos al expediente
/// del vertical Gobierno (doc gobierno.md §4). Desde T6 <b>persiste el binario de
/// verdad</b>: guarda los bytes en <see cref="IPrivateFileStore"/>, adjunta la metadata
/// (con el puntero al fichero) al expediente y deja rastro forense de la subida.
/// </summary>
/// <remarks>
/// <para>Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002): recibe <c>byte[]</c>, no <c>IFormFile</c>. NO duplica
/// estado: adjunta sobre el agregado de <see cref="StubApplicationService"/> por
/// composición (DIP). REUSA <see cref="IAuditTrailWriter"/> (ADR 0037): cada subida es
/// un evento append-only <c>gov.document-upload</c>.</para>
/// <para><b>El orden importa:</b> primero se guardan los BYTES, después se adjunta la
/// metadata. Al revés, un fallo del almacén dejaría en el expediente un documento que
/// se puede listar pero no descargar — la misma mentira que el email que logueaba
/// "enviado" sin enviar. El audit va al final y es best-effort (ADR 0037): el documento
/// ya está guardado y adjunto, un audit caído no puede tumbar la subida.</para>
/// </remarks>
public sealed class StubDocumentUploadService : IDocumentUploadService
{
    /// <summary>Carpeta lógica del almacén privado para los documentos de Gobierno.</summary>
    private const string FileScope = "gov-documents";

    private readonly StubApplicationService _cases;
    private readonly IPrivateFileStore _files;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;

    public StubDocumentUploadService(StubApplicationService cases, IPrivateFileStore files)
        : this(cases, files, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados) + time source
    /// inyectable para determinismo en tests (ADR 0002).
    /// </summary>
    public StubDocumentUploadService(
        StubApplicationService cases,
        IPrivateFileStore files,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CitizenDocumentRef> UploadAsync(
        string caseId,
        string name,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("El nombre del documento es obligatorio.", nameof(name));
        }
        if (content is null || content.Length == 0)
        {
            // Un adjunto vacío que respondiera "accepted" sería la mentira que T6 vino
            // a cerrar: éxito aparente sin nada detrás.
            throw new ArgumentException("El documento viene vacío.", nameof(content));
        }

        var detail = _cases.FindCase(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        // 1) Los BYTES primero: si esto falla, no se adjunta nada al expediente.
        var fileId = await _files.SaveAsync(
            FileScope,
            content,
            contentType,
            name.Trim(),
            cancellationToken).ConfigureAwait(false);

        var uploadedAt = _now();
        var doc = new CitizenDocumentRef(
            Id: $"doc_{Guid.NewGuid():N}",
            CaseId: detail.CaseId,
            Name: name.Trim(),
            Status: "accepted",
            UploadedAt: uploadedAt,
            FileId: fileId,
            ContentType: contentType,
            SizeBytes: content.Length);

        // 2) La metadata después: ya hay bytes que respaldan lo que se lista.
        _cases.AttachDocument(detail.CaseId, doc);

        // Rastro forense append-only de la subida (ADR 0037).
        if (_audit is not null)
        {
            // best-effort: el documento YA está adjunto y persistido.
            await BestEffort.RunAsync(() => _audit.WriteAsync(
                    new AuditEvent(
                        Id: doc.Id,
                        OccurredAtUtc: uploadedAt.UtcDateTime,
                        ActorEmail: detail.Citizen.Email,
                        ActorName: detail.Citizen.Name,
                        Action: "gov.document-upload",
                        Resource: detail.Radicado,
                        Outcome: "success",
                        Detail: $"Documento adjuntado: {doc.Name}."),
                    cancellationToken), cancellationToken);
        }

        return doc;
    }
}
