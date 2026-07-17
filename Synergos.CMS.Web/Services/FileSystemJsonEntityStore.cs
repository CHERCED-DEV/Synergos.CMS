using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IJsonEntityStore"/> — la ÚNICA implementación de persistencia
/// JSON durable del proyecto. Colapsa los cuatro stores FileSystem idénticos que T1
/// (órdenes de Tienda), T3 (sesiones de pago + reservas) y el fan-out de Booking
/// (órdenes de viaje) habían duplicado: misma escritura atómica, mismo fail-safe, mismo
/// saneo. Regla de oro del doc 25 — ninguna capacidad transversal se implementa dos veces.
/// </summary>
/// <remarks>
/// Persiste en <c>{StorageRoot}/syn-{resourceType}/{key}.json</c> — 1 archivo por entidad
/// (mutar una no toca las otras). Escritura ATÓMICA: temp único + <see cref="File.Move(string,string,bool)"/>
/// bajo lock (crash-safe). <c>Directory.CreateDirectory</c> lazy justo antes de escribir —
/// cero I/O en boot (ADR 0013). Lectura fail-safe: I/O corrupto se loguea y se trata como
/// ausente, jamás propaga (la escritura SÍ propaga: es la que debe ser crash-safe).
/// Single-instance (como el resto de los stores FS). Sin cifrado (PII de compra, no PHI).
/// </remarks>
public sealed class FileSystemJsonEntityStore : IJsonEntityStore
{
    private const string Extension = ".json";

    private readonly IHostEnvironment _env;
    private readonly ILogger<FileSystemJsonEntityStore> _logger;
    private readonly string _storageRoot;
    private readonly object _writeLock = new();

    public FileSystemJsonEntityStore(
        IOptions<JsonEntityStoreSettings> options,
        IHostEnvironment env,
        ILogger<FileSystemJsonEntityStore> logger)
    {
        _env = env;
        _logger = logger;
        _storageRoot = options.Value.StorageRoot;
    }

    public async Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(resourceType, key);
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

    public async Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(resourceType, key);
        if (!File.Exists(path)) return null;
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemJsonEntityStore: no se pudo leer {ResourceType}/{Key}", resourceType, key);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
    {
        var dir = DirPath(resourceType);
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

    public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
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
            _logger.LogWarning(ex, "FileSystemJsonEntityStore: no se pudo borrar {ResourceType}/{Key}", resourceType, key);
            return Task.FromResult(false);
        }
    }

    // {ContentRoot}/{StorageRoot}/syn-{resourceType}/ — el prefijo "syn-" preserva EXACTO
    // las rutas que ya usaban los stores dedicados (syn-orders, syn-payments,
    // syn-reservations, syn-travel-orders) → los datos existentes NO se huerfanizan.
    private string DirPath(string resourceType)
        => Path.Combine(_env.ContentRootPath, _storageRoot, "syn-" + Sanitize(resourceType));

    private string ResolvePath(string resourceType, string key)
        => Path.Combine(DirPath(resourceType), Sanitize(key) + Extension);

    // resourceType es una constante del código y las claves son servidor-generadas
    // ("ord_/stub_/resv_/trip_{guid:N}"), pero saneamos por defensa en profundidad para
    // que jamás escapen el directorio.
    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }
        return value.Replace("..", "-");
    }
}
