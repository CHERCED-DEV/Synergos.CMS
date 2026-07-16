using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPaymentSessionStore"/> (T3, doc 25). Persiste el JSON de cada
/// sesión de pago ATÓMICO (temp + <see cref="File.Move(string,string,bool)"/>) en
/// <c>{StorageRoot}/{sessionId}.json</c> — 1 archivo por sesión (mutación
/// Authorized→Captured sin tocar otras). Calca <see cref="FileSystemShopOrderStore"/>
/// (T1). Con esto la sesión SOBREVIVE un reinicio: un checkout hecho antes del reinicio
/// se puede capturar después. Fail-safe: I/O corrupto se ignora (log), nunca propaga.
/// Single-instance. PII de compra, no PHI → sin cifrar.
/// </summary>
public sealed class FileSystemPaymentSessionStore : IPaymentSessionStore
{
    private const string Extension = ".json";

    private readonly IHostEnvironment _env;
    private readonly ILogger<FileSystemPaymentSessionStore> _logger;
    private readonly string _storageRoot;
    private readonly object _writeLock = new();

    public FileSystemPaymentSessionStore(
        IOptions<PaymentSessionsSettings> options,
        IHostEnvironment env,
        ILogger<FileSystemPaymentSessionStore> logger)
    {
        _env = env;
        _logger = logger;
        _storageRoot = options.Value.StorageRoot;
    }

    public async Task WriteAsync(string sessionId, string json, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(sessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);   // lazy, nada en boot (ADR 0013)
        var tempPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tempPath, json, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
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
    }

    public async Task<string?> ReadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(sessionId);
        if (!File.Exists(path)) return null;
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemPaymentSessionStore: no se pudo leer la sesión {SessionId}", sessionId);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var dir = DirPath();
        if (!Directory.Exists(dir)) return Array.Empty<string>();

        var results = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*" + Extension))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                results.Add(await File.ReadAllTextAsync(file, Encoding.UTF8, cancellationToken).ConfigureAwait(false));
            }
            catch (IOException)
            {
                // archivo bloqueado/ilegible → saltar (el motor deserializa; el corrupto lo filtra)
            }
        }
        return results;
    }

    public Task<bool> DeleteAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(sessionId);
        if (!File.Exists(path)) return Task.FromResult(false);
        try
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemPaymentSessionStore: no se pudo borrar {SessionId}", sessionId);
            return Task.FromResult(false);
        }
    }

    private string DirPath() => Path.Combine(_env.ContentRootPath, _storageRoot);

    private string ResolvePath(string sessionId) => Path.Combine(DirPath(), Sanitize(sessionId) + Extension);

    // sessionId es servidor-generado ("stub_{guid:N}"), pero saneamos por defensa en
    // profundidad para que jamás escape el directorio.
    private static string Sanitize(string sessionId)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            sessionId = sessionId.Replace(c, '-');
        }
        return sessionId.Replace("..", "-");
    }
}
