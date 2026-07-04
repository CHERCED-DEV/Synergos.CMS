using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubDocumentUploadService"/> (seam <c>IDocumentUploadService</c>,
/// adjuntos del expediente del portal Gobierno): empty (sin archivos / expediente
/// desconocido) / happy (PDF aceptado queda adjunto + audita gov.document-upload) /
/// filter (tipo no permitido o peso &gt; 10 MB ⇒ rejected, NO se adjunta) / idempotent
/// (append-only: subir dos veces crea referencias distintas, sin sobrescribir).
/// ADR 0075.
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

    private static CitizenDocumentUpload Pdf(string name = "cedula.pdf")
        => new(name, "application/pdf", 350_000);

    [Fact] // empty: lista vacía o nula lanza ArgumentException
    public async Task Upload_NoFiles_Throws()
    {
        var (uploads, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-1001", Array.Empty<CitizenDocumentUpload>()));
    }

    [Fact] // empty: expediente desconocido lanza ArgumentException
    public async Task Upload_UnknownCase_Throws()
    {
        var (uploads, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-que-no-existe", new[] { Pdf() }));
    }

    [Fact] // happy: PDF válido se acepta, queda adjunto al expediente y se audita
    public async Task Upload_ValidPdf_AttachesAndAudits()
    {
        var (uploads, cases, audit) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        var result = await uploads.UploadAsync("case-1001", new[] { Pdf() });

        var doc = Assert.Single(result.Uploaded);
        Assert.Equal("accepted", doc.ValidationStatus);
        Assert.Equal("cedula.pdf", doc.FileName);
        Assert.StartsWith("doc_", doc.Id);
        Assert.Contains("case-1001", doc.SecureUrl);

        // El expediente ganó el adjunto (composición sobre el agregado, DIP).
        Assert.Equal(before + 1, cases.FindCase("case-1001")!.Documents.Count);

        // Rastro forense append-only de la subida (ADR 0037).
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.document-upload", evt.Action);
        Assert.Equal("success", evt.Outcome);
    }

    [Fact] // filter: tipo no permitido ⇒ rejected y NO se adjunta
    public async Task Upload_DisallowedType_IsRejected()
    {
        var (uploads, cases, audit) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        var result = await uploads.UploadAsync("case-1001", new List<CitizenDocumentUpload>
        {
            new("virus.exe", "application/x-msdownload", 1_000),
            Pdf(),
        });

        Assert.Equal(2, result.Uploaded.Count);
        var rejected = result.Uploaded.Single(d => d.FileName == "virus.exe");
        Assert.Equal("rejected", rejected.ValidationStatus);
        Assert.Equal(string.Empty, rejected.SecureUrl);

        // Solo el aceptado quedó adjunto; el rechazado no toca el expediente.
        Assert.Equal(before + 1, cases.FindCase("case-1001")!.Documents.Count);
        Assert.DoesNotContain(cases.FindCase("case-1001")!.Documents, d => d.FileName == "virus.exe");

        // Subida mixta ⇒ outcome parcial en la auditoría.
        Assert.Equal("partial", Assert.Single(audit.Events).Outcome);
    }

    [Fact] // filter: peso fuera de rango (0 o > 10 MB) ⇒ rejected
    public async Task Upload_InvalidSize_IsRejected()
    {
        var (uploads, _, _) = Make();

        var result = await uploads.UploadAsync("case-1001", new List<CitizenDocumentUpload>
        {
            new("gigante.pdf", "application/pdf", 11 * 1024 * 1024),
            new("vacio.pdf", "application/pdf", 0),
        });

        Assert.All(result.Uploaded, d => Assert.Equal("rejected", d.ValidationStatus));
    }

    [Fact] // filter: imágenes JPG/PNG también se aceptan (requisito Ventanillas CO)
    public async Task Upload_Images_AreAccepted()
    {
        var (uploads, _, _) = Make();

        var result = await uploads.UploadAsync("case-1001", new List<CitizenDocumentUpload>
        {
            new("foto.jpg", "image/jpeg", 250_000),
            new("firma.png", "image/png", 80_000),
        });

        Assert.All(result.Uploaded, d => Assert.Equal("accepted", d.ValidationStatus));
    }

    [Fact] // idempotent/append-only: subir dos veces crea referencias distintas
    public async Task Upload_Twice_AppendsDistinctRefs()
    {
        var (uploads, cases, _) = Make();

        var first = await uploads.UploadAsync("case-1001", new[] { Pdf() });
        var second = await uploads.UploadAsync("case-1001", new[] { Pdf() });

        Assert.NotEqual(first.Uploaded[0].Id, second.Uploaded[0].Id);
        Assert.Equal(2, cases.FindCase("case-1001")!.Documents.Count);
    }
}
