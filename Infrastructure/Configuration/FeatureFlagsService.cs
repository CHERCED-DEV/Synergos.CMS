using System.Reflection;
using Microsoft.Extensions.Options;
using Synergos.CMS.Configuration;
using Synergos.CMS.Domain.Services;

namespace Synergos.CMS.Infrastructure.Configuration;

/// <summary>
/// Reflection-driven adapter from <see cref="FeatureFlagsSettings"/> to
/// <see cref="IFeatureFlags"/>. Built once at construction (no per-call
/// reflection cost). Singleton lifetime — settings are hot-swappable via
/// <see cref="IOptionsMonitor{TOptions}"/> so we always read latest values.
/// </summary>
public sealed class FeatureFlagsService : IFeatureFlags
{
    private readonly IOptionsMonitor<FeatureFlagsSettings> _options;
    private static readonly PropertyInfo[] _properties =
        typeof(FeatureFlagsSettings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanRead)
            .ToArray();

    public FeatureFlagsService(IOptionsMonitor<FeatureFlagsSettings> options)
    {
        _options = options;
    }

    public bool IsEnabled(string flagName, bool defaultIfMissing = false)
    {
        if (string.IsNullOrWhiteSpace(flagName)) return defaultIfMissing;

        var prop = _properties.FirstOrDefault(p =>
            string.Equals(p.Name, flagName, StringComparison.OrdinalIgnoreCase));

        return prop is null
            ? defaultIfMissing
            : (bool)(prop.GetValue(_options.CurrentValue) ?? defaultIfMissing);
    }

    public IReadOnlyDictionary<string, bool> Snapshot()
    {
        var settings = _options.CurrentValue;
        return _properties.ToDictionary(
            p => p.Name,
            p => (bool)(p.GetValue(settings) ?? false),
            StringComparer.OrdinalIgnoreCase);
    }
}
