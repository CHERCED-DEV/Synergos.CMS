using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for Hero element — mirrors UI HeroElementData.
/// </summary>
public sealed record HeroElementData : BaseElementData
{
    public ContentHeading? Heading { get; init; }
    public ContentText? Text { get; init; }
    public ContentMedia? Media { get; init; }
    public ContentCta? Cta { get; init; }
}
