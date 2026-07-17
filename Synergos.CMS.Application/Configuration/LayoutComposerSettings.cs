namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:LayoutComposer</c>. Controls opt-in behaviours of the
/// Layout Composer (Ola 42.5+) that should not ship as defaults.
/// </summary>
/// <remarks>
/// <para>
/// Feature flags here are intentionally defensive: editors shouldn't
/// see auto-generated content unless a deploy explicitly wants it.
/// Every flag defaults to <c>false</c>; a deploy opts in via
/// <c>appsettings.json</c>:
/// </para>
/// <code>
/// "Synergos": {
///   "LayoutComposer": {
///     "EnableStarterScaffold": true
///   }
/// }
/// </code>
/// </remarks>
public sealed class LayoutComposerSettings
{
    /// <summary>
    /// When <c>true</c>, new <c>pageBase</c> content nodes are
    /// pre-populated with a minimal layout scaffold (Hero + 2ColEven +
    /// 1Col CTA) on first save if the <c>sections</c> property is
    /// empty. When <c>false</c> (default), the editor starts with a
    /// blank page — preserves surprise-free UX.
    /// </summary>
    public bool EnableStarterScaffold { get; init; } = false;
}
