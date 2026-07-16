using System.Text;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IReservationStore"/> (T3, doc 25). Persiste el JSON de cada
/// reserva ATÓMICO (temp + <see cref="File.Move(string,string,bool)"/>) en
/// <c>{StorageRoot}/{reservationId}.json</c> — 1 archivo por reserva. Calca
/// <see cref="FileSystemShopOrderStore"/> (T1). Con esto el hold SOBREVIVE un reinicio,
/// cerrando la brecha por la que confirmar una orden tras reinicio lanzaba "Reserva no
/// encontrada". Fail-safe: I/O corrupto se ignora (log), nunca propaga. Single-instance.
/// </summary>
public sealed class FileSystemReservationStore : IReservationStore
{
    private const string Extension = ".json";

    private readonly IHostEnvironment _env;
    private readonly ILogger<FileSystemReservationStore> _logger;
    private readonly string _storageRoot;
    private readonly object _writeLock = new();

    public FileSystemReservationStore(
        IOptions<ReservationsSettings> options,
        IHostEnvironment env,
        ILogger<FileSystemReservationStore> logger)
    {
        _env = env;
        _logger = logger;
        _storageRoot = options.Value.StorageRoot;
    }

    public async Task WriteAsync(string reservationId, string json, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(reservationId);
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

    public async Task<string?> ReadAsync(string reservationId, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(reservationId);
        if (!File.Exists(path)) return null;
        try
        {
            return await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemReservationStore: no se pudo leer la reserva {ReservationId}", reservationId);
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

    public Task<bool> DeleteAsync(string reservationId, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(reservationId);
        if (!File.Exists(path)) return Task.FromResult(false);
        try
        {
            File.Delete(path);
            return Task.FromResult(true);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "FileSystemReservationStore: no se pudo borrar {ReservationId}", reservationId);
            return Task.FromResult(false);
        }
    }

    private string DirPath() => Path.Combine(_env.ContentRootPath, _storageRoot);

    private string ResolvePath(string reservationId) => Path.Combine(DirPath(), Sanitize(reservationId) + Extension);

    // reservationId es servidor-generado ("resv_{guid:N}"), pero saneamos por defensa
    // en profundidad para que jamás escape el directorio.
    private static string Sanitize(string reservationId)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            reservationId = reservationId.Replace(c, '-');
        }
        return reservationId.Replace("..", "-");
    }
}
