using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPaymentEventStore"/> (T3, doc 25). Ledger durable de eventos de
/// webhook ya procesados, 1 archivo por <c>{provider}-{eventId}</c> en
/// <c>App_Data/syn-payment-events/</c>. La exclusividad atómica se logra con
/// <see cref="FileMode.CreateNew"/> — el SO garantiza que solo un llamado crea el
/// archivo, ganando la carrera de entregas duplicadas concurrentes SIN read-then-write
/// (que reintroduciría el TOCTOU). Única desviación del patrón temp+Move de T1.
/// </summary>
public sealed class FileSystemPaymentEventStore : IPaymentEventStore
{
    private const string SubDir = "App_Data/syn-payment-events/";

    private readonly IHostEnvironment _env;
    private readonly ILogger<FileSystemPaymentEventStore> _logger;

    public FileSystemPaymentEventStore(IHostEnvironment env, ILogger<FileSystemPaymentEventStore> logger)
    {
        _env = env;
        _logger = logger;
    }

    public Task<bool> TryMarkProcessedAsync(string provider, string eventId, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_env.ContentRootPath, SubDir);
        Directory.CreateDirectory(dir);   // lazy, nada en boot (ADR 0013)
        var path = Path.Combine(dir, Sanitize($"{provider}-{eventId}") + ".txt");

        try
        {
            // CreateNew = candado atómico: lanza IOException si el archivo YA existe.
            using var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var stamp = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            fs.Write(stamp, 0, stamp.Length);
            return Task.FromResult(true);   // ESTE llamado lo marcó → procesar side-effects
        }
        catch (IOException) when (File.Exists(path))
        {
            return Task.FromResult(false);  // ya existía → duplicado, no re-ejecutar
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // I/O real (disco lleno/permiso): NO se pudo marcar. Propaga para que el
            // receptor devuelva 5xx y el PSP reintente — jamás silenciar como duplicado.
            _logger.LogWarning(ex, "FileSystemPaymentEventStore: no se pudo marcar {Provider}/{EventId}", provider, eventId);
            throw;
        }
    }

    private static string Sanitize(string key)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            key = key.Replace(c, '-');
        }
        return key.Replace("..", "-");
    }
}
