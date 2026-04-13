using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for Media+Text Split element — mirrors UI MediaTextSplitElementData.
/// </summary>
public sealed record MediaTextSplitElementData : BaseElementData
{
    public ContentText? Text { get; init; }
    public ContentMedia? Media { get; init; }
    public ContentCta? Cta { get; init; }
}
