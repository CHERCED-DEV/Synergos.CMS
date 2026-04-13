using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for Rich Text element — mirrors UI RichTextElementData.
/// </summary>
public sealed record RichTextElementData : BaseElementData
{
    public ContentText? Text { get; init; }
}
