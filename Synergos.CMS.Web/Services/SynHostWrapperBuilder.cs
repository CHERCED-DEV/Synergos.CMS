using System.Text;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Builds the opening <c>&lt;div&gt;</c> wrapper that SynHost partials
/// emit around their <c>&lt;synergos-*&gt;</c> custom element so that
/// the compDom* editorial props (cssClass, variantKey,
/// hideOnMobile/Tablet/Desktop, cssDataAttributes, ariaRole, ariaLabel)
/// translate to real HTML output.
/// </summary>
/// <remarks>
/// <para>
/// The wrapper uses <c>syn-host</c> as base class plus BEM modifier
/// <c>syn-host--{variantKey}</c> and visibility utility classes
/// (<c>syn-hidden-mobile</c>, <c>syn-hidden-tablet</c>,
/// <c>syn-hidden-desktop</c>). Custom <c>cssClass</c> and
/// <c>cssDataAttributes</c> are appended verbatim.
/// </para>
/// <para>
/// ariaRole becomes <c>role</c>, ariaLabel becomes <c>aria-label</c>.
/// Both are skipped when empty so the CDN block can handle its own
/// semantics via its custom element implementation.
/// </para>
/// </remarks>
public static class SynHostWrapperBuilder
{
    public static SynHostWrapper Build(IPublishedElement model)
    {
        var modifiers = new List<string>();

        var variant = Val(model, "variantKey");
        if (variant is not null) modifiers.Add($"syn-host--{variant}");

        if (model.Value<bool>("hideOnMobile")) modifiers.Add("syn-hidden-mobile");
        if (model.Value<bool>("hideOnTablet")) modifiers.Add("syn-hidden-tablet");
        if (model.Value<bool>("hideOnDesktop")) modifiers.Add("syn-hidden-desktop");

        var cssClass = Val(model, "cssClass");
        if (cssClass is not null) modifiers.Add(cssClass);

        var attrs = new StringBuilder();
        var role = Val(model, "ariaRole");
        if (role is not null && role != "none") attrs.Append($" role=\"{Escape(role)}\"");
        var label = Val(model, "ariaLabel");
        if (label is not null) attrs.Append($" aria-label=\"{Escape(label)}\"");

        var dataAttrs = Val(model, "cssDataAttributes");
        if (dataAttrs is not null)
        {
            foreach (var line in dataAttrs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                if (!key.StartsWith("data-", StringComparison.OrdinalIgnoreCase)) key = "data-" + key;
                attrs.Append($" {Escape(key)}=\"{Escape(val)}\"");
            }
        }

        // When the editor hasn't set any compDom* prop, skip the
        // wrapper entirely — the custom element carries its own
        // semantics and we avoid DOM pollution.
        var needsWrapper = modifiers.Count > 0 || attrs.Length > 0;
        if (!needsWrapper) return new SynHostWrapper(string.Empty, string.Empty, IsNoOp:true);

        modifiers.Insert(0, "syn-host");
        return new SynHostWrapper(string.Join(' ', modifiers), attrs.ToString(), IsNoOp:false);
    }

    private static string? Val(IPublishedElement model, string alias)
    {
        var raw = model.Value<string>(alias);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static string Escape(string value) =>
        value.Replace("&", "&amp;").Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");
}

public sealed record SynHostWrapper(string Classes, string Attributes, bool IsNoOp = false);
