using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPhiStore"/> (ADR 0098). Cifra cada documento PHI con
/// <see cref="IDataProtector"/> (purpose <c>"Synergos.Healthcare.PHI.v1"</c>) y
/// lo escribe ATÓMICO (temp + <see cref="File.Move(string,string,bool)"/>) en
/// <c>App_Data/syn-healthcare/{resourceType}/{key}.phi</c>.
/// </summary>
/// <remarks>
/// Corrige el bug de los stores predecesores (<c>File.WriteAllText</c> no
/// atómico). Fail-safe: el contenido no descifrable (corrupto/manipulado) se
/// ignora (null) en vez de devolverse crudo. Disclaimer: cifrado at-rest
/// baseline, NO HIPAA-grade. Single-instance (ADR 0098 D1).
/// </remarks>
public sealed class FileSystemEncryptedPhiStore : IPhiStore
{
    private const string RootDir = "syn-healthcare";
    private const string Extension = ".phi";
    private const string ProtectorPurpose = "Synergos.Healthcare.PHI.v1";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly IDataProtector _protector;
    private readonly ILogger<FileSystemEncryptedPhiStore> _logger;
    private readonly object _writeLock = new();

    public FileSystemEncryptedPhiStore(
        IHostEnvironment hostEnvironment,
        IDataProtectionProvider dataProtectionProvider,
        ILogger<FileSystemEncryptedPhiStore> logger)
    {
        _hostEnvironment = hostEnvironment;
        _protector = dataProtectionProvider.CreateProtector(ProtectorPurpose);
        _logger = logger;
    }

    public async Task WriteAsync(string resourceType, Guid key, string json, CancellationToken cancellationToken)
    {
        var path = ResolvePath(resourceType, key);
        var cipher = _protector.Protect(json);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        // Temp ÚNICO por escritura: dos writes concurrentes a la misma key no
        // colisionan en el mismo .tmp. El File.Move atómico decide el ganador.
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, cipher, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
            lock (_writeLock)
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        finally
        {
            // Si el temp quedó (p.ej. Move falló), se limpia — sin huérfanos.
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch (IOException) { /* best-effort */ }
            }
        }
    }

    public async Task<string?> ReadAsync(string resourceType, Guid key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(resourceType, key);
        if (!File.Exists(path)) return null;
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return Decrypt(content);
    }

    public async Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken)
    {
        var dir = Path.Combine(_hostEnvironment.ContentRootPath, "App_Data", RootDir, Sanitize(resourceType));
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        var results = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*" + Extension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string content;
            try { content = await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken).ConfigureAwait(false); }
            catch (IOException) { continue; }
            var json = Decrypt(content);
            if (json is not null) results.Add(json);
        }
        return results;
    }

    public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken cancellationToken)
    {
        var path = ResolvePath(resourceType, key);
        if (!File.Exists(path)) return Task.FromResult(false);
        try
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemEncryptedPhiStore: no se pudo borrar {ResourceType}/{Key}", resourceType, key);
            return Task.FromResult(false);
        }
    }

    private string? Decrypt(string content)
    {
        try
        {
            return _protector.Unprotect(content);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            // Fail-safe PHI: contenido no descifrable (corrupto, manipulado o de
            // otra key) NO se devuelve crudo — podría ser PHI alterada o un ataque.
            // Se ignora (null) y se registra para que el operador lo detecte.
            _logger.LogWarning(ex,
                "FileSystemEncryptedPhiStore: contenido PHI no descifrable — ignorado (fail-safe, sin fuga de plaintext)");
            return null;
        }
    }

    private string ResolvePath(string resourceType, Guid key) => Path.Combine(
        _hostEnvironment.ContentRootPath, "App_Data", RootDir,
        Sanitize(resourceType), key.ToString("N") + Extension);

    // resourceType viene de constantes internas, pero saneamos por defensa en
    // profundidad para que jamás escape el directorio.
    private static string Sanitize(string resourceType)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            resourceType = resourceType.Replace(c, '-');
        }
        return resourceType.Replace("..", "-");
    }
}
