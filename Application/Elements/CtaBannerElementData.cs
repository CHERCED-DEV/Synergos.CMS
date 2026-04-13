using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for CTA Banner element — mirrors UI CtaBannerElementData.
/// </summary>
public sealed record CtaBannerElementData : BaseElementData
{
    public ContentText? Text { get; init; }
    public ContentCta? Cta { get; init; }
}
