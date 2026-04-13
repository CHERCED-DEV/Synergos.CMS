using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Components;

/// <summary>
/// Fallback ViewModel produced by GenericElementMapper for element types that
/// do not have a specific ISectionMapper registered.
///
/// Contains ALL content compositions (all nullable). The Angular renderer uses
/// the Alias field to select the matching component; only the properties that
/// were non-null on the CMS element will carry data.
///
/// This enables new element types to surface their composition data through the
/// JSON API without requiring a hand-crafted ISectionMapper — the composition
/// readers act as strategies applied universally.
/// </summary>
public sealed class GenericElementViewModel : BaseComponentViewModel
{
    private readonly string _alias;

    public GenericElementViewModel(string alias) => _alias = alias;

    public override string Alias      => _alias;
    public override string ViewName   => _alias;
    public override string BlockClass => $"sg-element--{_alias}";

    // ── Content compositions (all optional) ─────────────────────────────────
    public ContentHeadingModel? Heading    { get; init; }
    public ContentTextModel?    Text       { get; init; }
    public ContentMediaModel?   Media      { get; init; }
    public ContentCtaModel?     Cta        { get; init; }
    public ContentBadgeModel?   Badge      { get; init; }
    public ContentDateModel?    Date       { get; init; }
    public ContentEmbedModel?   Embed      { get; init; }
}
