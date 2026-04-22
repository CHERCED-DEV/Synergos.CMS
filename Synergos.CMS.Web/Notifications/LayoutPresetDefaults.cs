using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Pre-fills Layout Preset blocks (Ola 42.5) with sensible defaults on
/// save, so the editor sees configured presets without having to fill
/// every dropdown manually.
/// </summary>
/// <remarks>
/// <para>
/// Hook: <see cref="ContentSavingNotification"/>. For each saved
/// content we inspect every Block Grid value (raw JSON on the property)
/// and walk its <c>contentData</c> array. For entries whose
/// <c>contentTypeKey</c> matches one of the 10 Layout Preset
/// <c>elementLayout*</c> GUIDs we apply per-property defaults: any
/// chrome prop that is currently <em>empty</em> gets the default; any
/// prop the editor already set is preserved untouched.
/// </para>
/// <para>
/// Per-prop fill (vs the earlier "all-empty" heuristic) means the
/// defaults stick even across partial edits: the editor can change
/// <c>containerType</c> to "narrow" and the other four props still
/// get defaults on save. The trade-off: if the editor deliberately
/// clears a default to empty, the next save rewrites it — by design,
/// "empty" is treated as "missing", and a preset never ships without
/// a containerType/theme/spacing baseline. If a truly empty slot is
/// needed, the editor picks a specific value (e.g. "none" in
/// spacing).
/// </para>
/// </remarks>
public sealed class LayoutPresetDefaults
    : INotificationHandler<ContentSavingNotification>
{
    private const string BlockGridEditorAlias = "Umbraco.BlockGrid";

    // ContentTypeKey GUIDs of the 10 Layout Preset ElementTypes.
    // Match the Keys in uSync/v9/ContentTypes/elementlayout*.config.
    private static readonly HashSet<Guid> PresetContentTypeKeys = new()
    {
        Guid.Parse("1c68f4a9-24e9-49ac-9efa-05b3d4b1404a"), // Section
        Guid.Parse("f39c535a-879f-4bbf-8d94-8370c7f45f5a"), // Container
        Guid.Parse("c3dd2aaa-7cdf-410a-873e-2a36d52ecc39"), // Stack
        Guid.Parse("8247e825-1210-495a-a735-5ce8928fef07"), // Grid
        Guid.Parse("4b075799-e7ee-4164-aef8-21911360cfc1"), // Column
        Guid.Parse("57fc7792-c6d8-424b-be23-c7c217faedb3"), // 1Col
        Guid.Parse("e8baf208-35d5-4c9f-9aa4-967fa5e070bf"), // 2ColEven
        Guid.Parse("911a64ba-ccc4-4b21-a3f2-f9273c38c6b6"), // 2ColMainSidebar
        Guid.Parse("1fc59d8b-7278-4a0a-9b4c-d596ae230372"), // 3Col
        Guid.Parse("39e1538b-0ce7-40a3-9853-849354bb1c75"), // 4Col
    };

    private static readonly (string Alias, string Default)[] Defaults =
    {
        ("containerType",  "normal"),
        ("theme",          "light"),
        ("spacingTop",     "lg"),
        ("spacingBottom",  "lg"),
        ("spacingInline",  "md"),
    };

    public void Handle(ContentSavingNotification notification)
    {
        foreach (var content in notification.SavedEntities)
        {
            foreach (var property in content.Properties)
            {
                if (property.PropertyType.PropertyEditorAlias != BlockGridEditorAlias)
                {
                    continue;
                }

                // A BlockGrid value is culture/segment-variant; iterate
                // all stored variations so the handler works for
                // pageBase (Culture=es-CO + en-US) and pageBasic alike.
                foreach (var pValue in property.Values)
                {
                    var raw = pValue.EditedValue as string ?? pValue.PublishedValue as string;
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        continue;
                    }

                    var patched = TryApplyDefaults(raw);
                    if (patched is not null && !ReferenceEquals(patched, raw))
                    {
                        content.SetValue(property.Alias, patched, pValue.Culture, pValue.Segment);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Parses the Block Grid JSON, walks <c>contentData</c> looking for
    /// Layout Preset entries with all chrome props empty, and fills the
    /// defaults. Returns the modified JSON string when at least one
    /// entry was patched; otherwise returns <paramref name="raw"/>
    /// unchanged.
    /// </summary>
    internal static string? TryApplyDefaults(string raw)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            return raw; // malformed: don't touch, let Umbraco surface the error.
        }
        if (root is null)
        {
            return raw;
        }

        var contentData = root["contentData"] as JsonArray;
        if (contentData is null)
        {
            return raw;
        }

        var changed = false;
        foreach (var entry in contentData.OfType<JsonObject>())
        {
            if (!TryReadGuid(entry["contentTypeKey"], out var typeKey) ||
                !PresetContentTypeKeys.Contains(typeKey))
            {
                continue;
            }

            // Per-prop fill: any empty chrome prop gets its default.
            // Props the editor already set keep their value.
            foreach (var (alias, value) in Defaults)
            {
                if (IsEmpty(entry[alias]))
                {
                    entry[alias] = value;
                    changed = true;
                }
            }
        }

        return changed ? root.ToJsonString() : raw;
    }

    private static bool IsEmpty(JsonNode? node)
    {
        if (node is null) return true;
        var str = node.GetValue<string?>();
        return string.IsNullOrWhiteSpace(str);
    }

    private static bool TryReadGuid(JsonNode? node, out Guid value)
    {
        value = default;
        if (node is null) return false;
        var raw = node.GetValue<string?>();
        return !string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out value);
    }
}
