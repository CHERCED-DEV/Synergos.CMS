using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Sliding-window rate limiter in-memory para form submissions.
/// Conta intentos por <c>(ClientIp + FormKey)</c> dentro de la última
/// hora; rechaza cuando se supera <see cref="FormsSettings.MaxSubmissionsPerHourPerIp"/>.
/// </summary>
/// <remarks>
/// In-memory significa: el límite se resetea con el restart del proceso
/// y NO se comparte entre instancias bajo load balancer. Es un
/// best-effort razonable para sitios de tráfico bajo/medio.
///
/// Para multi-instancia, swappear con un adapter sobre Redis (TTL keys)
/// o sobre un middleware tipo `Microsoft.AspNetCore.RateLimiting`. La
/// seam pública (TryRegister) se preserva.
/// </remarks>
public sealed class InMemoryFormRateLimiter
{
    private static readonly TimeSpan WindowSize = TimeSpan.FromHours(1);

    private readonly IOptions<FormsSettings> _options;

    private readonly ConcurrentDictionary<string, List<DateTime>> _attempts =
        new(StringComparer.Ordinal);

    public InMemoryFormRateLimiter(IOptions<FormsSettings> options)
    {
        _options = options;
    }

    /// <summary>
    /// Registra un intento. Devuelve <c>true</c> cuando aún cabe en la
    /// ventana (caller debe procesar) y <c>false</c> cuando se supera
    /// el cap (caller debe responder 429).
    /// </summary>
    public bool TryRegister(string clientIp, string formKey)
    {
        if (string.IsNullOrWhiteSpace(clientIp) || string.IsNullOrWhiteSpace(formKey))
        {
            return true;
        }

        var key = $"{clientIp}|{formKey}";
        var now = DateTime.UtcNow;
        var max = _options.Value.MaxSubmissionsPerHourPerIp;

        var list = _attempts.GetOrAdd(key, _ => new List<DateTime>());
        lock (list)
        {
            list.RemoveAll(t => now - t > WindowSize);
            if (list.Count >= max)
            {
                return false;
            }
            list.Add(now);
            return true;
        }
    }
}
