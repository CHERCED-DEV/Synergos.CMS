using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Composicional contract for Testimonial List element — mirrors UI TestimonialListElementData.
/// </summary>
public sealed record TestimonialListElementData : BaseElementData
{
    public ContentCollection? Collection { get; init; }
}
