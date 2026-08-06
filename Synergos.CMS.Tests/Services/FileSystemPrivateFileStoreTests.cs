using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="FileSystemPrivateFileStore"/> (seam <c>IPrivateFileStore</c>, T6 — el
/// único sitio del proyecto donde se persiste un binario subido por un usuario):
/// empty (id desconocido / vacío → null) / happy (los bytes vuelven idénticos, con su
/// content-type y nombre) / filter (cada id devuelve SU fichero, sin mezclarse) /
/// idempotent (guardar dos veces el mismo contenido da ids distintos, no se pisan).
/// Más las dos propiedades de seguridad: <b>en disco NO queda el plaintext</b> y el
/// contenido manipulado se ignora en vez de devolverse crudo. ADR 0075.
/// </summary>
public sealed class FileSystemPrivateFileStoreTests : IDisposable
{
    private const string Scope = "gov-documents";
    private readonly string _root;
    private readonly FileSystemPrivateFileStore _store;

    public FileSystemPrivateFileStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "syn-t6-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);

        var env = Substitute.For<IHostEnvironment>();
        env.ContentRootPath.Returns(_root);

        // Data Protection efímero: cifra de verdad, sin depender del keyring del host.
        var protection = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(_root, "keys")));

        _store = new FileSystemPrivateFileStore(
            env,
            protection,
            Options.Create(new PrivateFileStoreSettings { StorageRoot = "App_Data/" }),
            NullLogger<FileSystemPrivateFileStore>.Instance);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* best-effort */ }
    }

    private string FilesDir => Path.Combine(_root, "App_Data", "syn-files", Scope);

    [Fact] // empty: un id que no existe (o vacío) devuelve null, no lanza
    public async Task Read_UnknownId_ReturnsNull()
    {
        Assert.Null(await _store.ReadAsync(Scope, Guid.NewGuid().ToString("N")));
        Assert.Null(await _store.ReadAsync(Scope, string.Empty));
    }

    [Fact] // happy: los bytes vuelven IDÉNTICOS, con su content-type y su nombre original
    public async Task Save_ThenRead_RoundTripsExactBytes()
    {
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00, 0xFF, 0x10 }; // binario, no texto

        var fileId = await _store.SaveAsync(Scope, content, "application/pdf", "cédula.pdf");
        var read = await _store.ReadAsync(Scope, fileId);

        Assert.NotNull(read);
        Assert.Equal(content, read!.Content);
        Assert.Equal("application/pdf", read.ContentType);
        Assert.Equal("cédula.pdf", read.FileName);
    }

    [Fact] // EL PUNTO DEL ALMACÉN: en disco NO queda el contenido en claro
    public async Task Save_WritesCiphertext_NotPlaintext()
    {
        var secret = Encoding.UTF8.GetBytes("NUMERO-DE-CEDULA-52841937");

        var fileId = await _store.SaveAsync(Scope, secret, "application/pdf", "cedula.pdf");

        var onDisk = await File.ReadAllBytesAsync(Path.Combine(FilesDir, fileId + ".bin"));
        var asText = Encoding.UTF8.GetString(onDisk);
        // Ni el contenido ni el nombre del fichero se leen del disco.
        Assert.DoesNotContain("52841937", asText, StringComparison.Ordinal);
        Assert.DoesNotContain("cedula.pdf", asText, StringComparison.Ordinal);
        Assert.NotEqual(secret, onDisk);
    }

    [Fact] // El id lo genera el ALMACÉN y es opaco: no sale del nombre que mandó el usuario
    public async Task Save_GeneratesOpaqueId_NotDerivedFromFileName()
    {
        var fileId = await _store.SaveAsync(Scope, new byte[] { 1 }, "image/png", "mi-foto.png");

        Assert.DoesNotContain("mi-foto", fileId, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(32, fileId.Length); // Guid "N"
    }

    [Fact] // filter: cada id devuelve SU fichero — dos adjuntos no se mezclan
    public async Task Read_ManyFiles_EachReturnsItsOwn()
    {
        var a = await _store.SaveAsync(Scope, Encoding.UTF8.GetBytes("AAA"), "application/pdf", "a.pdf");
        var b = await _store.SaveAsync(Scope, Encoding.UTF8.GetBytes("BBB"), "image/png", "b.png");

        Assert.Equal("AAA", Encoding.UTF8.GetString((await _store.ReadAsync(Scope, a))!.Content));
        Assert.Equal("BBB", Encoding.UTF8.GetString((await _store.ReadAsync(Scope, b))!.Content));
        Assert.Equal("image/png", (await _store.ReadAsync(Scope, b))!.ContentType);
    }

    [Fact] // idempotent: el mismo contenido dos veces da ficheros distintos, sin pisarse
    public async Task Save_SameContentTwice_DoesNotOverwrite()
    {
        var content = Encoding.UTF8.GetBytes("mismo");

        var first = await _store.SaveAsync(Scope, content, "application/pdf", "x.pdf");
        var second = await _store.SaveAsync(Scope, content, "application/pdf", "x.pdf");

        Assert.NotEqual(first, second);
        Assert.NotNull(await _store.ReadAsync(Scope, first));
        Assert.NotNull(await _store.ReadAsync(Scope, second));
    }

    [Fact] // fail-safe: un fichero manipulado se IGNORA, no se devuelve crudo
    public async Task Read_TamperedFile_ReturnsNull()
    {
        var fileId = await _store.SaveAsync(Scope, Encoding.UTF8.GetBytes("original"), "application/pdf", "x.pdf");
        var path = Path.Combine(FilesDir, fileId + ".bin");
        await File.WriteAllBytesAsync(path, Encoding.UTF8.GetBytes("esto no es el cifrado"));

        Assert.Null(await _store.ReadAsync(Scope, fileId));
    }

    [Fact] // El scope aísla: el mismo id bajo otro scope no devuelve el fichero
    public async Task Read_OtherScope_DoesNotLeak()
    {
        var fileId = await _store.SaveAsync(Scope, new byte[] { 7 }, "application/pdf", "x.pdf");

        Assert.Null(await _store.ReadAsync("otro-scope", fileId));
    }

    [Fact] // Delete: borra y el segundo intento dice false
    public async Task Delete_RemovesFile_AndIsIdempotent()
    {
        var fileId = await _store.SaveAsync(Scope, new byte[] { 1 }, "application/pdf", "x.pdf");

        Assert.True(await _store.DeleteAsync(Scope, fileId));
        Assert.Null(await _store.ReadAsync(Scope, fileId));
        Assert.False(await _store.DeleteAsync(Scope, fileId));
    }
}
