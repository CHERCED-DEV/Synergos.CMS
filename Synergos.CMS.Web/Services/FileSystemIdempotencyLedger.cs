using System.Text;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IIdempotencyLedger"/> — el ÚNICO candado de idempotencia durable del
/// proyecto. Generaliza el ledger que T3 creó para el webhook de pago; T4 (notificaciones)
/// es su segundo consumidor. 1 archivo por <c>(scope,key)</c> en
/// <c>App_Data/syn-{scope}/{key}.txt</c>.
/// </summary>
/// <remarks>
/// La exclusividad atómica se logra con <see cref="FileMode.CreateNew"/> — el SO garantiza
/// que solo un llamado crea el archivo, ganando la carrera SIN read-then-write (que
/// reintroduciría el TOCTOU). Es la razón por la que NO se colapsó en
/// <see cref="IJsonEntityStore"/> (upsert, sin compare-and-swap) — ADR 0105.
///
/// <para><b>Rutas y extensión preservadas del ledger de T3</b> (<c>syn-payment-events/</c>
/// + <c>.txt</c>): cambiarlas huerfanaría los marcadores ya escritos y el PSP re-procesaría
/// eventos ya atendidos.</para>
/// </remarks>
public sealed class FileSystemIdempotencyLedger : IIdempotencyLedger
{
    private const string StorageRoot = "App_Data/";
    private const string Extension = ".txt";

    private readonly IHostEnvironment _env;
    private readonly ILogger<FileSystemIdempotencyLedger> _logger;

    public FileSystemIdempotencyLedger(IHostEnvironment env, ILogger<FileSystemIdempotencyLedger> logger)
    {
        _env = env;
        _logger = logger;
    }

    public Task<bool> TryClaimAsync(string scope, string key, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_env.ContentRootPath, StorageRoot, "syn-" + Sanitize(scope));
        Directory.CreateDirectory(dir);   // lazy, nada en boot (ADR 0013)
        var path = Path.Combine(dir, Sanitize(key) + Extension);

        // CRÍTICO (defecto hallado por el review adversarial de T3): el
        // `catch when File.Exists` debe envolver SOLO el CreateNew. Si también cubriera el
        // Write, un write parcial fallido (disco lleno) tras un CreateNew exitoso vería
        // File.Exists==true y se clasificaría como "duplicado" — invirtiendo el fail-safe y
        // deduplicando permanentemente un side-effect que nunca ocurrió.
        FileStream fs;
        try
        {
            // CreateNew = candado atómico: lanza IOException si el archivo YA existe.
            // Aquí File.Exists==true implica inequívocamente preexistencia (duplicado).
            fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            return Task.FromResult(false);   // ya reclamada → duplicado
        }

        // A partir de aquí NOSOTROS creamos el archivo: ningún fallo puede ser "duplicado".
        try
        {
            var stamp = Encoding.UTF8.GetBytes(DateTimeOffset.UtcNow.ToString("O"));
            fs.Write(stamp, 0, stamp.Length);
            fs.Flush();
            fs.Dispose();
            return Task.FromResult(true);    // ESTE llamado la reclamó
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // I/O real tras crear el marcador: borra el parcial y propaga — jamás
            // silenciar como duplicado (el caller decide: 5xx y reintento).
            _logger.LogWarning(ex, "FileSystemIdempotencyLedger: fallo al escribir el marcador {Scope}/{Key}", scope, key);
            try { fs.Dispose(); } catch (IOException) { /* best-effort */ }
            try { File.Delete(path); } catch (IOException) { /* best-effort */ }
            throw;
        }
    }

    // scope es una constante del código y las claves son servidor-generadas, pero saneamos
    // por defensa en profundidad para que jamás escapen el directorio.
    private static string Sanitize(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(c, '-');
        }
        return value.Replace("..", "-");
    }
}
