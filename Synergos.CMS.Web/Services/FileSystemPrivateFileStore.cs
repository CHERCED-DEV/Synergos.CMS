using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPrivateFileStore"/> (T6). Cifra cada fichero con
/// <see cref="IDataProtector"/> y lo escribe ATÓMICO en
/// <c>{StorageRoot}/syn-files/{scope}/{fileId}.bin</c> — por defecto bajo
/// <c>App_Data/</c>, que <b>ningún middleware de estáticos sirve</b>.
/// </summary>
/// <remarks>
/// <para>Calca la mecánica probada de <see cref="FileSystemEncryptedPhiStore"/>
/// (ADR 0098): temp único por escritura + <see cref="File.Move(string,string,bool)"/>
/// bajo lock (dos writes concurrentes no colisionan y el Move decide el ganador),
/// <c>Sanitize</c> anti path-escape, y fail-safe al descifrar (contenido corrupto o de
/// otra key → <c>null</c>, nunca bytes crudos).</para>
/// <para>El <b>content-type y el nombre original viajan cifrados junto a los bytes</b>,
/// en una cabecera JSON de una línea. Guardarlos en el nombre del fichero los dejaría
/// en claro en el disco (y el nombre suele delatar el contenido: "cedula-juan.pdf").</para>
/// <para>El id lo genera el almacén (<c>Guid</c> N) — no se acepta del llamador, que
/// podría elegirlo adivinable o pisar un fichero ajeno.</para>
/// </remarks>
public sealed class FileSystemPrivateFileStore : IPrivateFileStore
{
    private const string RootDir = "syn-files";
    private const string Extension = ".bin";
    private const string ProtectorPurpose = "Synergos.PrivateFiles.v1";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IDataProtector _protector;
    private readonly ILogger<FileSystemPrivateFileStore> _logger;
    private readonly string _storageRoot;
    private readonly object _writeLock = new();

    public FileSystemPrivateFileStore(
        IHostEnvironment hostEnvironment,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<PrivateFileStoreSettings> options,
        ILogger<FileSystemPrivateFileStore> logger)
    {
        _hostEnvironment = hostEnvironment;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
        var configured = options.Value.StorageRoot;
        _storageRoot = string.IsNullOrWhiteSpace(configured) ? "App_Data/" : configured;
    }

    public async Task<string> SaveAsync(
        string scope,
        byte[] content,
        string contentType,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fileId = Guid.NewGuid().ToString("N");
        var path = ResolvePath(scope, fileId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // Cabecera (content-type + nombre) + bytes, todo dentro del mismo sobre cifrado:
        // el nombre del fichero en disco es solo el id opaco, no delata nada.
        var envelope = BuildEnvelope(content, contentType, fileName);
        var cipher = _protector.Protect(envelope);

        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(tempPath, cipher, cancellationToken).ConfigureAwait(false);
            lock (_writeLock)
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { /* best-effort */ }
            }
        }

        return fileId;
    }

    public async Task<PrivateFileContent?> ReadAsync(
        string scope,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        var path = ResolvePath(scope, fileId);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] cipher;
        try
        {
            cipher = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemPrivateFileStore: no se pudo leer {Scope}/{FileId}", scope, fileId);
            return null;
        }

        byte[] envelope;
        try
        {
            envelope = _protector.Unprotect(cipher);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Fail-safe: contenido no descifrable (corrupto, manipulado o de otra key)
            // NO se devuelve crudo. Se ignora y se registra para que el operador lo vea.
            _logger.LogWarning(ex,
                "FileSystemPrivateFileStore: fichero no descifrable — ignorado (fail-safe) {Scope}/{FileId}",
                scope, fileId);
            return null;
        }

        return ParseEnvelope(envelope);
    }

    public Task<bool> DeleteAsync(string scope, string fileId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return Task.FromResult(false);
        }

        var path = ResolvePath(scope, fileId);
        if (!File.Exists(path))
        {
            return Task.FromResult(false);
        }

        try
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemPrivateFileStore: no se pudo borrar {Scope}/{FileId}", scope, fileId);
            return Task.FromResult(false);
        }
    }

    // ── Sobre: {json}\n{bytes} ───────────────────────────────────────────────────
    // Una línea JSON con la cabecera + '\n' + el binario tal cual. Se busca el PRIMER
    // '\n' para partir: el JSON no lleva saltos y los bytes que vengan después pueden
    // contener cualquier cosa sin ambigüedad.

    private static byte[] BuildEnvelope(byte[] content, string contentType, string fileName)
    {
        var header = JsonSerializer.Serialize(new FileHeader(contentType, fileName));
        var headerBytes = Encoding.UTF8.GetBytes(header);
        var envelope = new byte[headerBytes.Length + 1 + content.Length];
        headerBytes.CopyTo(envelope, 0);
        envelope[headerBytes.Length] = (byte)'\n';
        content.CopyTo(envelope, headerBytes.Length + 1);
        return envelope;
    }

    private PrivateFileContent? ParseEnvelope(byte[] envelope)
    {
        var separator = Array.IndexOf(envelope, (byte)'\n');
        if (separator <= 0)
        {
            _logger.LogWarning("FileSystemPrivateFileStore: sobre sin cabecera — ignorado (fail-safe)");
            return null;
        }

        FileHeader? header;
        try
        {
            header = JsonSerializer.Deserialize<FileHeader>(
                Encoding.UTF8.GetString(envelope, 0, separator));
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "FileSystemPrivateFileStore: cabecera ilegible — ignorada (fail-safe)");
            return null;
        }

        if (header is null)
        {
            return null;
        }

        var contentStart = separator + 1;
        var content = new byte[envelope.Length - contentStart];
        Array.Copy(envelope, contentStart, content, 0, content.Length);

        return new PrivateFileContent(
            content,
            string.IsNullOrWhiteSpace(header.ContentType) ? "application/octet-stream" : header.ContentType,
            string.IsNullOrWhiteSpace(header.FileName) ? "documento" : header.FileName);
    }

    private sealed record FileHeader(string ContentType, string FileName);

    private string ResolvePath(string scope, string fileId) => Path.Combine(
        _hostEnvironment.ContentRootPath, _storageRoot, RootDir,
        Sanitize(scope), Sanitize(fileId) + Extension);

    // scope y fileId salen de constantes internas y de Guid.NewGuid(), pero se sanean
    // por defensa en profundidad para que jamás escapen del directorio.
    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "default";
        }
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }
        return value.Replace("..", "-");
    }
}
