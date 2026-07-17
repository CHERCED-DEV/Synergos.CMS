using System;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubDocumentUploadService"/> (seam <c>IDocumentUploadService</c>,
/// adjuntos del expediente del portal Gobierno): empty (nombre vacío / expediente
/// desconocido) / happy (documento queda adjunto + audita gov.document-upload) / filter
/// (se resuelve el expediente por caseId Y por radicado) / idempotent (append-only:
/// subir dos veces crea referencias distintas, sin sobrescribir). ADR 0075.
/// </summary>
public class StubDocumentUploadServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static (StubDocumentUploadService Uploads, StubApplicationService Cases, RecordingAuditTrailWriter Audit) Make()
    {
        var audit = new RecordingAuditTrailWriter();
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            null,
            () => Clock);
        return (new StubDocumentUploadService(cases, audit, () => Clock), cases, audit);
    }

    [Fact] // empty: nombre vacío lanza ArgumentException
    public async Task Upload_NoName_Throws()
    {
        var (uploads, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-1001", string.Empty));
    }

    [Fact] // empty: expediente desconocido lanza ArgumentException
    public async Task Upload_UnknownCase_Throws()
    {
        var (uploads, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-que-no-existe", "cedula.pdf"));
    }

    [Fact] // happy: documento válido se acepta, queda adjunto al expediente y se audita
    public async Task Upload_ValidDoc_AttachesAndAudits()
    {
        var (uploads, cases, audit) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        var doc = await uploads.UploadAsync("case-1001", "cedula.pdf");

        Assert.Equal("accepted", doc.Status);
        Assert.Equal("cedula.pdf", doc.Name);
        Assert.StartsWith("doc_", doc.Id);

        // El expediente ganó el adjunto (composición sobre el agregado, DIP).
        Assert.Equal(before + 1, cases.FindCase("case-1001")!.Documents.Count);

        // Rastro forense append-only de la subida (ADR 0037).
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.document-upload", evt.Action);
        Assert.Equal("success", evt.Outcome);
    }

    [Fact] // filter: el documento se adjunta resolviendo el expediente por radicado
    public async Task Upload_ByRadicado_Attaches()
    {
        var (uploads, cases, _) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        await uploads.UploadAsync("SG-2026-001001", "recibo.pdf");

        Assert.Equal(before + 1, cases.FindCase("case-1001")!.Documents.Count);
    }

    [Fact] // idempotent/append-only: subir dos veces crea referencias distintas
    public async Task Upload_Twice_AppendsDistinctRefs()
    {
        var (uploads, cases, _) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        var first = await uploads.UploadAsync("case-1001", "a.pdf");
        var second = await uploads.UploadAsync("case-1001", "b.pdf");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(before + 2, cases.FindCase("case-1001")!.Documents.Count);
    }
}
