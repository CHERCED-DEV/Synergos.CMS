using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for Card element — mirrors UI CardElementData.
/// </summary>
public sealed record CardElementData : BaseElementData
{
    public ContentText? Text { get; init; }
    public ContentMedia? Media { get; init; }
    public ContentCta? Cta { get; init; }
    public ContentBadge? Badge { get; init; }
}
