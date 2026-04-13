using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for Feature Grid element — mirrors UI FeatureGridElementData.
/// </summary>
public sealed record FeatureGridElementData : BaseElementData
{
    public ContentCollection? Collection { get; init; }
}
