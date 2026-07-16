using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="ITravelOrderStore"/> (fan-out de T1, doc 25). Persiste el JSON de
/// cada orden de viaje ATÓMICO (temp + <see cref="File.Move(string,string,bool)"/>) en
/// <c>{StorageRoot}/{orderRef}.json</c> — 1 archivo por orden. Calca
/// <see cref="FileSystemShopOrderStore"/> (T1). Con esto un carrito de viaje confirmado
/// SOBREVIVE un reinicio (las reservas y el pago ya lo hacían por T3). Fail-safe: I/O
/// corrupto se ignora (log), nunca propaga. Single-instance.
/// </summary>
public sealed class FileSystemTravelOrderStore : ITravelOrderStore
{
    private const string Extension = ".json";

    private readonly IHostEnvironment _env;
    private readonly ILogger<FileSystemTravelOrderStore> _logger;
    private readonly string _storageRoot;
    private readonly object _writeLock = new();

    public FileSystemTravelOrderStore(
        IOptions<TravelOrdersSettings> options,
        IHostEnvironment env,
        ILogger<FileSystemTravelOrderStore> logger)
    {
        _env = env;
        _logger = logger;
        _storageRoot = options.Value.StorageRoot;
    }

    public async Task WriteAsync(string orderRef, string json, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(orderRef);
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

    public async Task<string?> ReadAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(orderRef);
        if (!File.Exists(path)) return null;
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemTravelOrderStore: no se pudo leer la orden {OrderRef}", orderRef);
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

    public Task<bool> DeleteAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(orderRef);
        if (!File.Exists(path)) return Task.FromResult(false);
        try
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemTravelOrderStore: no se pudo borrar {OrderRef}", orderRef);
            return Task.FromResult(false);
        }
    }

    private string DirPath() => Path.Combine(_env.ContentRootPath, _storageRoot);

    private string ResolvePath(string orderRef) => Path.Combine(DirPath(), Sanitize(orderRef) + Extension);

    // orderRef es servidor-generado ("trip_{guid:N}"), pero saneamos por defensa en
    // profundidad para que jamás escape el directorio.
    private static string Sanitize(string orderRef)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            orderRef = orderRef.Replace(c, '-');
        }
        return orderRef.Replace("..", "-");
    }
}
