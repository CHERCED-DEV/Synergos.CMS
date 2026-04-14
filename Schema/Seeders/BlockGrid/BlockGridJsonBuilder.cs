using System.Text.Json;
using System.Text.Json.Nodes;

namespace Synergos.CMS.Schema.Seeders.BlockGrid;

/// <summary>
/// Stateful builder that assembles an Umbraco BlockGrid JSON document used as
/// the storage representation of <c>pageSections</c> properties.
///
/// Owns two parallel buffers — <c>layout</c> (visible structure) and
/// <c>contentData</c> (block payload by UDI). Each <see cref="AddRow"/> call
/// appends to both. <see cref="ToJson"/> emits the canonical Umbraco shape.
///
/// Hides three concerns from callers:
///   - UDI formatting (<c>umb://element/{guid:N}</c>).
///   - Area key formatting (<c>{guid:D}</c>).
///   - JSON value coercion (string / int / bool / nested JsonNode).
///
/// All block UDIs are auto-generated via <see cref="Guid.NewGuid"/> — there is
/// no idempotency benefit to deterministic UDIs because the seeder skips a
/// page when its <c>pageSections</c> property already has any value.
/// </summary>
internal sealed class BlockGridJsonBuilder
{
    private const string BlockGridEditorAlias = "Umbraco.BlockGrid";
    private const int    DefaultColumnSpan    = 12;

    private readonly JsonArray _layout      = new();
    private readonly JsonArray _contentData = new();

    /// <summary>
    /// Appends a layout-preset row containing one or more named areas.
    /// Each area lists the items rendered inside it (already added to
    /// <see cref="_contentData"/> by the caller).
    /// </summary>
    public BlockGridJsonBuilder AddRow(
        Guid presetUdi,
        int  columnSpan,
        params (Guid AreaKey, IReadOnlyList<Guid> Items)[] areas)
    {
        var areaArray = new JsonArray();
        foreach (var (areaKey, items) in areas)
        {
            var itemArray = new JsonArray();
            foreach (var item in items)
            {
                itemArray.Add(new JsonObject
                {
                    ["contentUdi"] = ToUdi(item),
                    ["columnSpan"] = DefaultColumnSpan,
                    ["rowSpan"]    = 1,
                    ["areas"]      = new JsonArray()
                });
            }
            areaArray.Add(new JsonObject
            {
                ["key"]   = ToAreaKey(areaKey),
                ["items"] = itemArray
            });
        }

        _layout.Add(new JsonObject
        {
            ["contentUdi"] = ToUdi(presetUdi),
            ["columnSpan"] = columnSpan,
            ["rowSpan"]    = 1,
            ["areas"]      = areaArray
        });
        return this;
    }

    /// <summary>
    /// Appends a content entry. <paramref name="contentTypeKey"/> is the GUID of the
    /// element type (use <c>ContentTypeKeys.X.ToString("D")</c>); <paramref name="contentUdi"/>
    /// is the per-instance UDI (call <see cref="Guid.NewGuid"/> at the call site).
    /// Property values undergo lightweight coercion to JsonValue / JsonNode.
    /// </summary>
    public BlockGridJsonBuilder AddContent(
        Guid                                  contentTypeKey,
        Guid                                  contentUdi,
        IReadOnlyDictionary<string, object?>? props = null)
    {
        var entry = new JsonObject
        {
            ["contentTypeKey"] = contentTypeKey.ToString("D"),
            ["udi"]            = ToUdi(contentUdi)
        };

        if (props is not null)
        {
            foreach (var (alias, value) in props)
                entry[alias] = ToJsonValue(value);
        }

        _contentData.Add(entry);
        return this;
    }

    /// <summary>
    /// Convenience for layout-preset entries — they have no editable properties,
    /// only a <c>contentTypeKey</c> + UDI pair.
    /// </summary>
    public BlockGridJsonBuilder AddLayoutPreset(Guid contentTypeKey, Guid presetUdi)
        => AddContent(contentTypeKey, presetUdi);

    /// <summary>Serializes the builder to the canonical BlockGrid JSON.</summary>
    public string ToJson()
    {
        var root = new JsonObject
        {
            ["layout"]       = new JsonObject { [BlockGridEditorAlias] = _layout },
            ["contentData"]  = _contentData,
            ["settingsData"] = new JsonArray()
        };
        return root.ToJsonString();
    }

    // ── Formatting helpers ──────────────────────────────────────────────────

    private static string ToUdi(Guid g)     => $"umb://element/{g:N}";
    private static string ToAreaKey(Guid g) => g.ToString("D");

    /// <summary>
    /// Coerces a property value to its canonical JsonNode representation.
    /// Already-built JsonNodes are deep-cloned to avoid sharing.
    /// </summary>
    private static JsonNode? ToJsonValue(object? value) => value switch
    {
        null         => null,
        JsonNode n   => n.DeepClone(),
        string s     => JsonValue.Create(s),
        int i        => JsonValue.Create(i),
        bool b       => JsonValue.Create(b),
        _            => JsonValue.Create(value.ToString())
    };

    // ── Stored-value helpers consumers might want ───────────────────────────

    /// <summary>
    /// Builds the JSON-array stored representation for an Umbraco
    /// DropDown.Flexible single-value property — e.g. <c>["dark"]</c>.
    /// </summary>
    public static string DropdownValue(string value)
        => JsonSerializer.Serialize(new[] { value });

    /// <summary>
    /// Builds the JSON-array stored representation for an Umbraco
    /// MultiUrlPicker single-link property — e.g. <c>[{"url":…}]</c>.
    /// </summary>
    public static JsonNode SingleLink(string url, string name, string target = "_self")
        => JsonNode.Parse(
            $"[{{\"url\":\"{url}\",\"name\":\"{name}\",\"target\":\"{target}\",\"udi\":null}}]")!;
}
