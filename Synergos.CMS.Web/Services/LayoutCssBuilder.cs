using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Builds BEM-flavoured CSS modifier classes from the Ola 42 layout
/// compositions (<c>compDomDisplay</c>, <c>compDomFlex</c>,
/// <c>compDomGrid</c>) on an <see cref="IPublishedElement"/>.
/// </summary>
/// <remarks>
/// Each composition contributes a distinct <c>syn-display--</c>,
/// <c>syn-flex--</c>, or <c>syn-grid--</c> namespace. The element
/// renderer joins the result with its own base class (e.g.
/// <c>syn-section</c>) to produce the final <c>class</c> attribute.
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
        // compDomVisibility: la sección de layout la trae en su esquema y el render no la leía, así que «Ocultar en
        // móvil» no ocultaba nada. Las mismas clases que SynHostWrapperBuilder (syn-utilities.css). Con las tres, la
        // sección queda desconectada sin salir del contenido (012-blogs-social, D-012-V9).
        if (model.Value<bool>("hideOnMobile")) yield return "syn-hidden-mobile";
        if (model.Value<bool>("hideOnTablet")) yield return "syn-hidden-tablet";
        if (model.Value<bool>("hideOnDesktop")) yield return "syn-hidden-desktop";

        var mobileCollapse = Val(model, "mobileCollapseMode");
        if (mobileCollapse is not null && mobileCollapse != "auto")
        {
            // "auto" is the implicit default — no class emitted so the
            // design system CSS handles it with baseline media queries.
            yield return $"syn-preset--mobile-{mobileCollapse}";
        }
    }

    /// <summary>
    /// El espaciado a los lados de las partes fijas de una plantilla de página —la cabecera de un artículo, sus etiquetas,
    /// sus relacionados—, de la misma composición que el de una sección (<c>compDomSpacing</c>): el que eligió el editor,
    /// o <paramref name="porDefecto"/>, el del preset de sección, si no eligió ninguno (012-blogs-social, D-012-V13).
    /// </summary>
    public static string EspaciadoLateral(IPublishedElement model, IPublishedValueFallback fallback, string porDefecto = "lg")
    {
        var valor = model.Value<string>(fallback, "spacingInline");
        return $"syn-space--inline-{(string.IsNullOrWhiteSpace(valor) ? porDefecto : valor.Trim())}";
    }

    private static string? Val(IPublishedElement model, string alias)
    {
        var raw = model.Value<string>(alias);
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }
}
