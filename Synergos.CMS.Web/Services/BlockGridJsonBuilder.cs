using System.Text.Json;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Builder fluent para serializar JSON de Umbraco.BlockGrid (Layout
/// Composer) desde código. Genera la shape canónica que Umbraco 13
/// espera en propiedades tipo BlockGrid:
/// <code>
/// {
///   "layout": { "Umbraco.BlockGrid": [ { contentUdi, areas, ... } ] },
///   "contentData": [ { contentTypeKey, udi, &lt;alias&gt;: &lt;value&gt; } ],
///   "settingsData": []
/// }
/// </code>
/// </summary>
/// <remarks>
/// Dev-only por ahora — consumido por <see cref="SynergosIdentitySeeder"/>
/// gated por <c>Synergos:DevSeed:Enabled=true</c>. No es API pública del
/// CMS ni se expone a editors.
/// </remarks>
public sealed class BlockGridJsonBuilder
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = false };

    private readonly List<LayoutItem> _layout = new();
    private readonly List<Dictionary<string, object>> _contentData = new();
    private readonly List<Dictionary<string, object>> _settingsData = new();

    /// <summary>
    /// Agrega un bloque top-level con sus areas anidadas opcionales.
    /// </summary>
    public BlockBuilder AddTopLevelBlock(Guid contentTypeKey, int columnSpan = 12, int rowSpan = 1)
    {
        var udi = NewElementUdi();
        var layoutItem = new LayoutItem
        {
            ContentUdi = udi,
            ColumnSpan = columnSpan,
            RowSpan = rowSpan,
            Areas = new List<LayoutArea>(),
        };
        _layout.Add(layoutItem);

        var content = new Dictionary<string, object>
        {
            ["contentTypeKey"] = contentTypeKey.ToString(),
            ["udi"] = udi,
        };
        _contentData.Add(content);

        return new BlockBuilder(this, layoutItem, content);
    }

    public string Build()
    {
        var root = new Dictionary<string, object>
        {
            ["layout"] = new Dictionary<string, object>
            {
                ["Umbraco.BlockGrid"] = _layout.Select(SerializeLayoutItem).ToList(),
            },
            ["contentData"] = _contentData,
            ["settingsData"] = _settingsData,
        };
        return JsonSerializer.Serialize(root, SerializerOptions);
    }

    private static Dictionary<string, object?> SerializeLayoutItem(LayoutItem item)
    {
        return new Dictionary<string, object?>
        {
            ["contentUdi"] = item.ContentUdi,
            ["settingsUdi"] = null,
            ["columnSpan"] = item.ColumnSpan,
            ["rowSpan"] = item.RowSpan,
            ["areas"] = item.Areas.Select(SerializeArea).ToList(),
        };
    }

    private static Dictionary<string, object> SerializeArea(LayoutArea area)
    {
        return new Dictionary<string, object>
        {
            ["key"] = area.Key.ToString(),
            ["items"] = area.Items.Select(SerializeLayoutItem).ToList(),
        };
    }

    private static string NewElementUdi() => $"umb://element/{Guid.NewGuid():N}";

    internal sealed class LayoutItem
    {
        public string ContentUdi { get; set; } = string.Empty;
        public int ColumnSpan { get; set; }
        public int RowSpan { get; set; }
        public List<LayoutArea> Areas { get; set; } = new();
    }

    internal sealed class LayoutArea
    {
        public Guid Key { get; set; }
        public List<LayoutItem> Items { get; set; } = new();
    }

    /// <summary>
    /// Fluent builder devuelto por <see cref="AddTopLevelBlock"/>.
    /// Permite poblar property values y agregar items anidados en areas.
    /// </summary>
    public sealed class BlockBuilder
    {
        private readonly BlockGridJsonBuilder _parent;
        private readonly LayoutItem _layoutItem;
        private readonly Dictionary<string, object> _contentEntry;

        internal BlockBuilder(
            BlockGridJsonBuilder parent,
            LayoutItem layoutItem,
            Dictionary<string, object> contentEntry)
        {
            _parent = parent;
            _layoutItem = layoutItem;
            _contentEntry = contentEntry;
        }

        public BlockBuilder Set(string propertyAlias, object? value)
        {
            if (value is null) return this;
            // El editor Umbraco.TrueFalse almacena Int32 (1/0). Un bool .NET se
            // serializaría como "True"/"False" (string) y rompe el indexado Examine
            // ("Cannot assign value 'True' ... expecting System.Int32"). Normalizamos.
            _contentEntry[propertyAlias] = value is bool b ? (b ? 1 : 0) : value;
            return this;
        }

        /// <summary>
        /// Rellena props aún no seteadas con valores editor-safe (típicamente
        /// los defaults multi-value <c>"[]"</c> de <see cref="SchemaBlockDefaults"/>).
        /// No pisa valores ya establecidos vía <see cref="Set"/>, así que es
        /// seguro llamarlo antes o después de los Set de contenido real.
        /// </summary>
        public BlockBuilder ApplyDefaults(IReadOnlyDictionary<string, object> defaults)
        {
            foreach (var kv in defaults)
            {
                if (!_contentEntry.ContainsKey(kv.Key))
                {
                    _contentEntry[kv.Key] = kv.Value;
                }
            }
            return this;
        }

        /// <summary>
        /// Agrega un bloque hijo dentro del area indicada por su Key.
        /// El Key viene del DataType (DTBlockGridSections) donde se
        /// definen las areas permitidas por block.
        /// </summary>
        public BlockBuilder AddChild(
            Guid areaKey,
            Guid childContentTypeKey,
            Action<BlockBuilder> configure,
            int columnSpan = 12,
            int rowSpan = 1)
        {
            var area = _layoutItem.Areas.FirstOrDefault(a => a.Key == areaKey);
            if (area is null)
            {
                area = new LayoutArea { Key = areaKey, Items = new List<LayoutItem>() };
                _layoutItem.Areas.Add(area);
            }

            var childUdi = NewElementUdi();
            var childLayout = new LayoutItem
            {
                ContentUdi = childUdi,
                ColumnSpan = columnSpan,
                RowSpan = rowSpan,
                Areas = new List<LayoutArea>(),
            };
            area.Items.Add(childLayout);

            var childContent = new Dictionary<string, object>
            {
                ["contentTypeKey"] = childContentTypeKey.ToString(),
                ["udi"] = childUdi,
            };
            _parent._contentData.Add(childContent);

            var childBuilder = new BlockBuilder(_parent, childLayout, childContent);
            configure(childBuilder);
            return this;
        }
    }
}
