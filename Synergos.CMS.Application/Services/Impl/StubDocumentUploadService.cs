using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IDocumentUploadService"/> — subida STUB de documentos al
/// expediente del vertical Gobierno (doc gobierno.md §4). Adjunta un documento por
/// nombre (la UI envía solo el nombre; el binario NO viaja en la demo — subir PDF es
/// requisito explícito de las Ventanillas Únicas CO), lo marca <c>accepted</c>, lo
/// adjunta al expediente y deja rastro forense de la subida.
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
public sealed class StubDocumentUploadService : IDocumentUploadService
{
    private readonly StubApplicationService _cases;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;

    public StubDocumentUploadService(StubApplicationService cases)
        : this(cases, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados) + time source
    /// inyectable para determinismo en tests (ADR 0002).
    /// </summary>
    public StubDocumentUploadService(StubApplicationService cases, IAuditTrailWriter? audit, Func<DateTimeOffset>? now)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public async Task<CitizenDocumentRef> UploadAsync(
        string caseId,
        string name,
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

        var detail = _cases.FindCase(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        var uploadedAt = _now();
        var doc = new CitizenDocumentRef(
            Id: $"doc_{Guid.NewGuid():N}",
            CaseId: detail.CaseId,
            Name: name.Trim(),
            Status: "accepted",
            UploadedAt: uploadedAt);

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
