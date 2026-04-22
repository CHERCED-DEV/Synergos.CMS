using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Builds BEM-flavoured CSS modifier classes from the Ola 42 layout
/// compositions (<c>compDomDisplay</c>, <c>compDomFlex</c>,
/// <c>compDomGrid</c>) on an <see cref="IPublishedElement"/>.
/// </summary>
/// <remarks>
/// <para>
/// Each composition contributes a distinct <c>syn-display--</c>,
/// <c>syn-flex--</c>, or <c>syn-grid--</c> namespace. The element
/// renderer joins the result with its own base class (e.g.
/// <c>syn-section</c>) to produce the final <c>class</c> attribute.
/// </para>
/// <para>
/// Backward compatibility: when the Ola 42 properties are empty but
/// the element carries the legacy <c>compDomLayout</c>
/// (<c>layoutDirection</c> / <c>layoutAlign</c>), the helper emits
/// equivalent flex modifiers so the visual output is preserved until
/// editors move on to the dropdown-driven composition set.
/// </para>
/// </remarks>
public static class LayoutCssBuilder
{
    public static IEnumerable<string> Build(IPublishedElement model)
    {
        var display = Val(model, "displayMode");
        if (display is not null) yield return $"syn-display--{display}";

        var flexDirection = Val(model, "flexDirection");
        var flexWrap = Val(model, "flexWrap");
        var justifyContent = Val(model, "justifyContent");
        var flexAlignItems = Val(model, "flexAlignItems");
        var alignContent = Val(model, "alignContent");
        var flexGap = Val(model, "flexGap");

        if (flexDirection is not null) yield return $"syn-flex--dir-{flexDirection}";
        if (flexWrap is not null) yield return $"syn-flex--wrap-{flexWrap}";
        if (justifyContent is not null) yield return $"syn-flex--justify-{justifyContent}";
        if (flexAlignItems is not null) yield return $"syn-flex--align-items-{flexAlignItems}";
        if (alignContent is not null) yield return $"syn-flex--align-content-{alignContent}";
        if (flexGap is not null) yield return $"syn-flex--gap-{flexGap}";

        var gridTemplate = Val(model, "gridTemplate");
        var gridRows = Val(model, "gridRows");
        var justifyItems = Val(model, "justifyItems");
        var gridAlignItems = Val(model, "gridAlignItems");
        var gridGap = Val(model, "gridGap");
        var gridColumnGap = Val(model, "gridColumnGap");
        var gridRowGap = Val(model, "gridRowGap");

        if (gridTemplate is not null) yield return $"syn-grid--tpl-{gridTemplate}";
        if (gridRows is not null) yield return $"syn-grid--rows-{gridRows}";
        if (justifyItems is not null) yield return $"syn-grid--justify-items-{justifyItems}";
        if (gridAlignItems is not null) yield return $"syn-grid--align-items-{gridAlignItems}";
        if (gridGap is not null) yield return $"syn-grid--gap-{gridGap}";
        if (gridColumnGap is not null) yield return $"syn-grid--col-gap-{gridColumnGap}";
        if (gridRowGap is not null) yield return $"syn-grid--row-gap-{gridRowGap}";

        // Legacy compDomLayout fallback: only emit when the Ola 42
        // equivalents are absent, so editors that already moved on to
        // the new composition set aren't overridden.
        var legacyDir = Val(model, "layoutDirection");
        if (flexDirection is null && legacyDir is not null)
        {
            yield return $"syn-flex--dir-{legacyDir}";
        }
        var legacyAlign = Val(model, "layoutAlign");
        if (flexAlignItems is null && gridAlignItems is null && legacyAlign is not null)
        {
            yield return $"syn-flex--align-items-{legacyAlign}";
        }

        var spacingTop = Val(model, "spacingTop");
        var spacingBottom = Val(model, "spacingBottom");
        var spacingInline = Val(model, "spacingInline");
        if (spacingTop is not null) yield return $"syn-space--top-{spacingTop}";
        if (spacingBottom is not null) yield return $"syn-space--bottom-{spacingBottom}";
        if (spacingInline is not null) yield return $"syn-space--inline-{spacingInline}";

        // compDomPresetChrome (Ola 42.5) — exclusive to elementLayout* presets.
        var containerType = Val(model, "containerType");
        if (containerType is not null) yield return $"syn-preset--container-{containerType}";
        var theme = Val(model, "theme");
        if (theme is not null) yield return $"syn-preset--theme-{theme}";
        if (model.Value<bool>("noPaddingTop")) yield return "syn-preset--no-pad-top";
        if (model.Value<bool>("noPaddingBottom")) yield return "syn-preset--no-pad-bottom";
        if (model.Value<bool>("noMarginTop")) yield return "syn-preset--no-mar-top";
        if (model.Value<bool>("noMarginBottom")) yield return "syn-preset--no-mar-bottom";
        var mobileCollapse = Val(model, "mobileCollapseMode");
        if (mobileCollapse is not null && mobileCollapse != "auto")
        {
            // "auto" is the implicit default — no class emitted so the
            // design system CSS handles it with baseline media queries.
            yield return $"syn-preset--mobile-{mobileCollapse}";
        }
    }

    private static string? Val(IPublishedElement model, string alias)
    {
        var raw = model.Value<string>(alias);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
