using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Implementación in-memory de <see cref="ICartAbandonmentTracker"/>.
/// ConcurrentDictionary&lt;cartId, ActivityRecord&gt; con flag
/// reportado para no duplicar eventos.
/// </summary>
/// <remarks>
/// In-memory significa: el estado se pierde con restart del proceso y
/// NO se comparte entre instancias bajo load balancer. Aceptable para
/// sites de tráfico bajo/medio. Para producción multi-instancia, swap
/// con adapter sobre Redis/SQL — la seam pública (MarkActivity /
/// DetectAbandoned / MarkCompleted) se preserva.
/// </remarks>
public sealed class InMemoryCartAbandonmentTracker : ICartAbandonmentTracker
{
    private sealed class ActivityRecord
    {
        public DateTime LastActivityUtc { get; set; }
        public int ItemCount { get; set; }
        public decimal Subtotal { get; set; }
        public string Currency { get; set; } = "USD";
        public bool Reported { get; set; }
        public bool Completed { get; set; }
    }

    private readonly ConcurrentDictionary<string, ActivityRecord> _records = new(StringComparer.Ordinal);

    public void MarkActivity(string cartId, int itemCount, decimal subtotal, string currency)
    {
        if (string.IsNullOrWhiteSpace(cartId))
        {
            return;
        }

        _records.AddOrUpdate(
            key: cartId,
            addValueFactory: _ => new ActivityRecord
            {
                LastActivityUtc = DateTime.UtcNow,
                ItemCount = itemCount,
                Subtotal = subtotal,
                Currency = currency,
                Reported = false,
                Completed = false,
            },
            updateValueFactory: (_, existing) =>
            {
                existing.LastActivityUtc = DateTime.UtcNow;
                existing.ItemCount = itemCount;
                existing.Subtotal = subtotal;
                existing.Currency = currency;
                // Cualquier actividad nueva resetea el flag — si el
                // visitante volvió a interactuar tras un report
                // previo, vale reportar de nuevo si vuelve a
                // abandonar.
                existing.Reported = false;
                existing.Completed = false;
                return existing;
            });
    }

    public void MarkCompleted(string cartId)
    {
        if (string.IsNullOrWhiteSpace(cartId))
        {
            return;
        }

        if (_records.TryGetValue(cartId, out var record))
        {
            record.Completed = true;
        }
    }

    public IReadOnlyList<AbandonedCart> DetectAbandoned(TimeSpan threshold)
    {
        var nowUtc = DateTime.UtcNow;
        var abandoned = new List<AbandonedCart>();

        foreach (var (cartId, record) in _records)
        {
            if (record.Completed || record.Reported)
            {
                continue;
            }

            if (nowUtc - record.LastActivityUtc < threshold)
            {
                continue;
            }

            if (record.ItemCount <= 0)
            {
                // Cart vacío no es abandonment significativo.
                continue;
            }

            abandoned.Add(new AbandonedCart(
                CartId: cartId,
                ItemCount: record.ItemCount,
                Subtotal: record.Subtotal,
                Currency: record.Currency,
                LastActivityUtc: record.LastActivityUtc));

            record.Reported = true;
        }

        return abandoned;
    }
}
