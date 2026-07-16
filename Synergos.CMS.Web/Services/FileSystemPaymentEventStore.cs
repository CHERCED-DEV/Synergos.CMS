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

        // CRÍTICO: el `catch when File.Exists` debe envolver SOLO el CreateNew. Si
        // también cubriera el Write, un write parcial fallido (disco lleno) tras un
        // CreateNew exitoso vería File.Exists==true y se clasificaría como "duplicado"
        // — invirtiendo el fail-safe y deduplicando permanentemente una orden sin
        // confirmar. Por eso el CreateNew va en su propio try.
        FileStream fs;
        try
        {
            // CreateNew = candado atómico: lanza IOException si el archivo YA existe.
            // Aquí File.Exists==true implica inequívocamente preexistencia (duplicado).
            fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            return Task.FromResult(false);  // ya existía → duplicado, no re-ejecutar
        }

        // A partir de aquí NOSOTROS creamos el archivo: ningún fallo puede ser "duplicado".
        try
        {
            var stamp = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            fs.Write(stamp, 0, stamp.Length);
            fs.Flush();
            fs.Dispose();
            return Task.FromResult(true);   // ESTE llamado lo marcó → procesar side-effects
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // I/O real tras crear el marcador: borra el parcial de 0 bytes y propaga
            // para que el receptor devuelva 5xx y el PSP reintente limpio — jamás
            // silenciar como duplicado.
            _logger.LogWarning(ex, "FileSystemPaymentEventStore: fallo al escribir el marcador {Provider}/{EventId}", provider, eventId);
            try { fs.Dispose(); } catch (IOException) { /* best-effort */ }
            try { File.Delete(path); } catch (IOException) { /* best-effort */ }
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
