using System.Globalization;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IAnalyticsTracker"/>. Emite los eventos como log
/// estructurado con scope <c>"Analytics"</c>. Operadores agregan los
/// eventos vía cualquier sink estándar (Serilog console + sink Elastic /
/// Loki / Application Insights / etc).
/// </summary>
/// <remarks>
/// Sync, fire-and-forget — el log write es buffered por
/// <see cref="ILogger"/> y no bloquea el caller. Cero overhead bajo
/// load alto.
/// </remarks>
public sealed class LoggerAnalyticsTracker : IAnalyticsTracker
{
    private readonly ILogger<LoggerAnalyticsTracker> _logger;

    public LoggerAnalyticsTracker(ILogger<LoggerAnalyticsTracker> logger) =>
        _logger = logger;

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        using var scope = _logger.BeginScope("Analytics");
        if (properties is null || properties.Count == 0)
        {
            _logger.LogInformation("Event: {EventName}", eventName);
            return;
        }

        var serialized = string.Join(", ",
            properties.Select(p => $"{p.Key}={Format(p.Value)}"));
        _logger.LogInformation("Event: {EventName} {Properties}", eventName, serialized);
    }

    // Culture-invariant formatting — los logs son consumidos por
    // pipelines de ingestion (Logstash/Vector/Splunk) que parsean
    // números asumiendo punto decimal. Si el host corre con
    // CurrentCulture=es-CO (o cualquier locale con coma decimal),
    // value.ToString() emitiría "0,7629" rompiendo el parser.
    // IFormattable cubre numéricos (int/long/double/decimal/float)
    // + DateTimeOffset + TimeSpan + Guid en una sola branch.
    private static string Format(object? value) => value switch
    {
        null => "null",
        string s => $"\"{s}\"",
        DateTime d => d.ToString("O", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "null",
    };
}
