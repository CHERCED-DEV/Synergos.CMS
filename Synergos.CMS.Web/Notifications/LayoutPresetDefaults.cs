using System.Text.Json;
using System.Text.Json.Nodes;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Pre-fills Layout Preset blocks (Ola 42.5) with sensible defaults on
/// first save, so the editor sees a configured preset after dropping
/// it — not empty dropdowns that have to be filled every time.
/// </summary>
/// <remarks>
/// <para>
/// Hook: <see cref="ContentSavingNotification"/>. For each saved
/// content we inspect every Block Grid value (raw JSON on the property)
/// and walk its <c>contentData</c> array. When a block's
/// <c>contentTypeKey</c> matches one of the 10 Layout Preset
/// <c>elementLayout*</c> GUIDs AND none of the five chrome props
/// (<c>containerType</c>, <c>theme</c>, <c>spacingTop</c>,
/// <c>spacingBottom</c>, <c>spacingInline</c>) carry any value yet,
/// we assume the block was freshly dropped and fill the defaults.
/// </para>
/// <para>
/// The all-empty heuristic avoids clobbering deliberate editor choices:
/// once any chrome prop has been set (including explicitly cleared via
/// a later edit that sets another prop), the handler no longer rewrites.
/// Edge case: an editor who clears <em>every</em> chrome prop at once
/// will re-trigger the defaults on next save — acceptable trade-off for
/// a pure server-side solution (no flag in Umbraco for "just created").
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

            if (!AllChromePropsEmpty(entry))
            {
                continue; // editor has already touched this block.
            }

            foreach (var (alias, value) in Defaults)
            {
                entry[alias] = value;
            }
            changed = true;
        }

        return changed ? root.ToJsonString() : raw;
    }

    private static bool AllChromePropsEmpty(JsonObject entry)
    {
        foreach (var (alias, _) in Defaults)
        {
            var node = entry[alias];
            if (node is null) continue;
            var str = node.GetValue<string?>();
            if (!string.IsNullOrWhiteSpace(str))
            {
                return false;
            }
        }
        return true;
    }

    private static bool TryReadGuid(JsonNode? node, out Guid value)
    {
        value = default;
        if (node is null) return false;
        var raw = node.GetValue<string?>();
        return !string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out value);
    }
}
