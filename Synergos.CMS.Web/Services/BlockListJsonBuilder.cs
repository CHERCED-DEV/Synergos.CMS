using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Builder fluent para serializar JSON de Umbraco.BlockList desde código.
/// Shape canónica que Umbraco 13 espera en props BlockList anidadas
/// (ej. <c>elementCompHero.ctaItems</c>, <c>elementCompFeatureGrid.features</c>):
/// <code>
/// {
///   "layout": { "Umbraco.BlockList": [ { "contentUdi": "umb://element/.." } ] },
///   "contentData": [ { "contentTypeKey": "..", "udi": "..", &lt;alias&gt;: &lt;value&gt; } ],
///   "settingsData": []
/// }
/// </code>
/// Un BlockList anidado VACÍO no pasa la validación de publish; debe llevar
/// ≥1 item real con sus props (mandatory + multi-value via SchemaBlockDefaults).
/// Dev-only (ADR 0093).
/// </summary>
public sealed class BlockListJsonBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly List<string> _udis = new();
    private readonly List<Dictionary<string, object>> _contentData = new();

    public bool HasItems => _contentData.Count > 0;

    public ItemBuilder AddBlock(Guid contentTypeKey)
    {
        var udi = $"umb://element/{Guid.NewGuid():N}";
        _udis.Add(udi);
        var entry = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["contentTypeKey"] = contentTypeKey.ToString(),
            ["udi"] = udi,
        };
        _contentData.Add(entry);
        return new ItemBuilder(entry);
    }

    public string Build()
    {
        var root = new Dictionary<string, object>
        {
            ["layout"] = new Dictionary<string, object>
            {
                ["Umbraco.BlockList"] = _udis.Select(u => new Dictionary<string, object> { ["contentUdi"] = u }).ToList(),
            },
            ["contentData"] = _contentData,
            ["settingsData"] = new List<object>(),
        };
        return JsonSerializer.Serialize(root, SerializerOptions);
    }

    public sealed class ItemBuilder
    {
        private readonly Dictionary<string, object> _entry;

        internal ItemBuilder(Dictionary<string, object> entry) => _entry = entry;

        public ItemBuilder Set(string propertyAlias, object? value)
        {
            if (value is not null) { _entry[propertyAlias] = value; }
            return this;
        }

        public ItemBuilder ApplyDefaults(IReadOnlyDictionary<string, object> defaults)
        {
            foreach (var kv in defaults)
            {
                if (!_entry.ContainsKey(kv.Key)) { _entry[kv.Key] = kv.Value; }
            }
            return this;
        }
    }
}
