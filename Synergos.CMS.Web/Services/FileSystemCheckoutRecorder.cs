using System.Text;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="ICheckoutRecorder"/> (ADR 0097). Persiste JSONL
/// append-only en <c>App_Data/syn-dashboard/orders/{yyyy-MM-dd}.jsonl</c>
/// (un archivo por día, idempotente por <c>OrderId</c>). Espeja el patrón de
/// <see cref="FileSystemAuditTrailWriter"/>.
/// </summary>
/// <remarks>
/// <see cref="Record"/> es fire-and-forget: nunca lanza (log + swallow). El
/// read-model del dashboard agrega los pedidos a una serie de ingresos vía
/// <see cref="GetCheckouts"/>. Para volumen alto, swap por adapter sobre DB.
/// </remarks>
public sealed class FileSystemCheckoutRecorder : ICheckoutRecorder
{
    private const string DirectorySegment1 = "syn-dashboard";
    private const string DirectorySegment2 = "orders";

    private readonly IHostEnvironment _hostEnvironment;
    private readonly ILogger<FileSystemCheckoutRecorder> _logger;
    private readonly object _writeLock = new();

    public FileSystemCheckoutRecorder(
        IHostEnvironment hostEnvironment,
        ILogger<FileSystemCheckoutRecorder> logger)
    {
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    public void Record(CheckoutCompleted checkout)
    {
        if (checkout is null || string.IsNullOrWhiteSpace(checkout.OrderId))
        {
            return;
        }
        try
        {
            var path = ResolvePath(checkout.OccurredUtc);
            var line = JsonSerializer.Serialize(checkout) + Environment.NewLine;

            lock (_writeLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path, Encoding.UTF8);
                    if (existing.Contains($"\"OrderId\":\"{checkout.OrderId}\"", StringComparison.Ordinal))
                    {
                        return;
                    }
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FileSystemCheckoutRecorder: no se pudo registrar el checkout {OrderId}",
                checkout.OrderId);
        }
    }

    public IReadOnlyList<CheckoutCompleted> GetCheckouts(DateTime fromUtc, DateTime toUtc)
    {
        var fromDate = fromUtc.Date;
        var toDate = toUtc.Date;
        var results = new List<CheckoutCompleted>();

        foreach (var file in ListJsonlFiles())
        {
            var name = Path.GetFileNameWithoutExtension(file);
            if (!DateTime.TryParseExact(name, "yyyy-MM-dd", null,
                    System.Globalization.DateTimeStyles.None, out var fileDate)
                || fileDate < fromDate || fileDate > toDate)
            {
                continue;
            }

            string[] lines;
            try { lines = File.ReadAllLines(file, Encoding.UTF8); }
            catch (IOException) { continue; }

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                CheckoutCompleted? order;
                try { order = JsonSerializer.Deserialize<CheckoutCompleted>(line); }
                catch (JsonException) { continue; }
                if (order is null) continue;
                if (order.OccurredUtc < fromUtc || order.OccurredUtc > toUtc) continue;
                results.Add(order);
            }
        }

        return results.OrderByDescending(o => o.OccurredUtc).ToList();
    }

    private IEnumerable<string> ListJsonlFiles()
    {
        var dir = Path.Combine(
            _hostEnvironment.ContentRootPath, "App_Data", DirectorySegment1, DirectorySegment2);
        if (!Directory.Exists(dir)) return Array.Empty<string>();
        return Directory.EnumerateFiles(dir, "*.jsonl");
    }

    private string ResolvePath(DateTime occurredAtUtc)
    {
        var fileName = occurredAtUtc.ToString("yyyy-MM-dd") + ".jsonl";
        return Path.Combine(
            _hostEnvironment.ContentRootPath, "App_Data", DirectorySegment1, DirectorySegment2, fileName);
    }
}
