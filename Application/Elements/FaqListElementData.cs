using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for FAQ List element — mirrors UI FaqListElementData.
/// </summary>
public sealed record FaqListElementData : BaseElementData
{
    public ContentCollection? Collection { get; init; }
}
