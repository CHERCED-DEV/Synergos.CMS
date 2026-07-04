using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IDocumentUploadService"/> — subida STUB de documentos al
/// expediente del vertical Gobierno (doc gobierno-app-spec §4). Valida cada archivo
/// (tipo PDF/imagen + peso máximo 10 MB — subir PDF es requisito explícito de las
/// Ventanillas Únicas CO), crea una referencia por archivo con su
/// <c>ValidationStatus</c> (<c>accepted | rejected</c>), adjunta SOLO las aceptadas al
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
public sealed class StubDocumentUploadService : IDocumentUploadService
{
    private const long MaxSizeBytes = 10 * 1024 * 1024; // 10 MB

    private static readonly string[] AllowedContentTypes =
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
    };

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

    public async Task<DocumentUploadResult> UploadAsync(
        string caseId,
        IReadOnlyList<CitizenDocumentUpload> files,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }
        if (files is null || files.Count == 0)
        {
            throw new ArgumentException("Se requiere al menos un archivo.", nameof(files));
        }

        var detail = _cases.FindCase(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        var refs = new List<CitizenDocumentRef>(files.Count);
        foreach (var file in files)
        {
            if (file is null || string.IsNullOrWhiteSpace(file.FileName))
            {
                throw new ArgumentException("Cada archivo requiere un nombre.", nameof(files));
            }

            var id = $"doc_{Guid.NewGuid():N}";
            var accepted = IsValid(file);
            refs.Add(new CitizenDocumentRef(
                Id: id,
                CaseId: detail.CaseId,
                FileName: file.FileName.Trim(),
                ContentType: file.ContentType?.Trim() ?? string.Empty,
                SizeBytes: file.SizeBytes,
                ValidationStatus: accepted ? "accepted" : "rejected",
                SecureUrl: accepted ? $"/api/gov/case/{detail.CaseId}/document/{id}" : string.Empty));
        }

        var acceptedRefs = refs.Where(r => r.ValidationStatus == "accepted").ToList();
        _cases.AttachDocuments(detail.CaseId, acceptedRefs);

        // Rastro forense append-only de la subida (ADR 0037).
        if (_audit is not null)
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: $"upl_{Guid.NewGuid():N}",
                    OccurredAtUtc: _now().UtcDateTime,
                    ActorEmail: detail.Citizen.Email,
                    ActorName: detail.Citizen.Name,
                    Action: "gov.document-upload",
                    Resource: detail.Radicado,
                    Outcome: acceptedRefs.Count == refs.Count ? "success" : "partial",
                    Detail: $"{acceptedRefs.Count}/{refs.Count} documento(s) aceptado(s)."),
                cancellationToken);
        }

        return new DocumentUploadResult(refs);
    }

    // Validación pura/determinista: tipo permitido (PDF/JPG/PNG) + peso (0 < n ≤ 10 MB).
    private static bool IsValid(CitizenDocumentUpload file)
        => file.SizeBytes > 0
            && file.SizeBytes <= MaxSizeBytes
            && AllowedContentTypes.Contains(file.ContentType?.Trim(), StringComparer.OrdinalIgnoreCase);
}
