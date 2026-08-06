using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Almacén privado en memoria para tests: guarda los bytes tal cual y devuelve un id
/// opaco, igual que el de disco. Permite afirmar lo que T6 vino a cerrar — que los
/// BYTES existen de verdad, no solo la metadata.
/// </summary>
internal sealed class InMemoryPrivateFileStore : IPrivateFileStore
{
    private readonly Dictionary<string, PrivateFileContent> _files = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, PrivateFileContent> Files => _files;

    public Task<string> SaveAsync(string scope, byte[] content, string contentType, string fileName, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid().ToString("N");
        _files[$"{scope}/{id}"] = new PrivateFileContent(content, contentType, fileName);
        return Task.FromResult(id);
    }

    public Task<PrivateFileContent?> ReadAsync(string scope, string fileId, CancellationToken cancellationToken = default)
        => Task.FromResult(_files.TryGetValue($"{scope}/{fileId}", out var f) ? f : null);

    public Task<bool> DeleteAsync(string scope, string fileId, CancellationToken cancellationToken = default)
        => Task.FromResult(_files.Remove($"{scope}/{fileId}"));
}

/// <summary>
/// Cubre <see cref="StubDocumentUploadService"/> (seam <c>IDocumentUploadService</c>,
/// adjuntos del expediente del portal Gobierno): empty (nombre vacío / expediente
/// desconocido / contenido vacío) / happy (los BYTES quedan guardados + el documento
/// adjunto + audita gov.document-upload) / filter (se resuelve el expediente por caseId
/// Y por radicado) / idempotent (append-only: subir dos veces crea referencias y
/// ficheros distintos). ADR 0075.
/// </summary>
public class StubDocumentUploadServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private const string Pdf = "application/pdf";

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private static (StubDocumentUploadService Uploads, StubApplicationService Cases, RecordingAuditTrailWriter Audit, InMemoryPrivateFileStore Files) Make()
    {
        var audit = new RecordingAuditTrailWriter();
        var files = new InMemoryPrivateFileStore();
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            null,
            () => Clock);
        return (new StubDocumentUploadService(cases, files, audit, () => Clock), cases, audit, files);
    }

    [Fact] // empty: nombre vacío lanza ArgumentException
    public async Task Upload_NoName_Throws()
    {
        var (uploads, _, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-1001", string.Empty, Pdf, Bytes("x")));
    }

    [Fact] // empty: expediente desconocido lanza ArgumentException
    public async Task Upload_UnknownCase_Throws()
    {
        var (uploads, _, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-que-no-existe", "cedula.pdf", Pdf, Bytes("x")));
    }

    [Fact] // empty: un adjunto SIN bytes no puede responder "accepted" — es la mentira que T6 cierra
    public async Task Upload_EmptyContent_Throws()
    {
        var (uploads, cases, _, _) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        await Assert.ThrowsAsync<ArgumentException>(() =>
            uploads.UploadAsync("case-1001", "vacio.pdf", Pdf, Array.Empty<byte>()));

        // Y no dejó rastro en el expediente.
        Assert.Equal(before, cases.FindCase("case-1001")!.Documents.Count);
    }

    [Fact] // happy: los BYTES quedan guardados y el documento apunta a ellos
    public async Task Upload_ValidDoc_StoresBytes_AttachesAndAudits()
    {
        var (uploads, cases, audit, files) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;
        var content = Bytes("%PDF-1.7 contenido de la cedula");

        var doc = await uploads.UploadAsync("case-1001", "cedula.pdf", Pdf, content);

        Assert.Equal("accepted", doc.Status);
        Assert.Equal("cedula.pdf", doc.Name);
        Assert.StartsWith("doc_", doc.Id);

        // LO QUE T6 VINO A CERRAR: hay bytes de verdad detrás, no solo metadata.
        Assert.False(string.IsNullOrWhiteSpace(doc.FileId));
        Assert.Equal(Pdf, doc.ContentType);
        Assert.Equal(content.Length, doc.SizeBytes);
        var stored = await files.ReadAsync("gov-documents", doc.FileId!);
        Assert.NotNull(stored);
        Assert.Equal(content, stored!.Content);
        Assert.Equal("cedula.pdf", stored.FileName);

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
        var (uploads, cases, _, _) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        await uploads.UploadAsync("SG-2026-001001", "recibo.pdf", Pdf, Bytes("recibo"));

        Assert.Equal(before + 1, cases.FindCase("case-1001")!.Documents.Count);
    }

    [Fact] // idempotent/append-only: subir dos veces crea referencias y FICHEROS distintos
    public async Task Upload_Twice_AppendsDistinctRefsAndFiles()
    {
        var (uploads, cases, _, files) = Make();
        var before = cases.FindCase("case-1001")!.Documents.Count;

        var first = await uploads.UploadAsync("case-1001", "a.pdf", Pdf, Bytes("aaa"));
        var second = await uploads.UploadAsync("case-1001", "b.pdf", Pdf, Bytes("bbb"));

        Assert.NotEqual(first.Id, second.Id);
        // El segundo no pisa al primero: dos ficheros, con su contenido propio.
        Assert.NotEqual(first.FileId, second.FileId);
        Assert.Equal(2, files.Files.Count);
        Assert.Equal(Bytes("aaa"), (await files.ReadAsync("gov-documents", first.FileId!))!.Content);
        Assert.Equal(Bytes("bbb"), (await files.ReadAsync("gov-documents", second.FileId!))!.Content);
        Assert.Equal(before + 2, cases.FindCase("case-1001")!.Documents.Count);
    }
}
