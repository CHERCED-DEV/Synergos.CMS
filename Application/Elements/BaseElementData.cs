using Synergos.CMS.Domain.Compositions.Dom;
using Synergos.CMS.Domain.Compositions.Behavior;

namespace Synergos.CMS.Application.Elements;

/// <summary>
/// Base contract inherited by all element data contracts.
/// Mirrors UI BaseElementData — every element can carry DOM and behavior compositions.
/// </summary>
public abstract record BaseElementData
{
    public DomClass? DomClass { get; init; }
    public DomAttributes? DomAttributes { get; init; }
    public DomVariant? DomVariant { get; init; }
    public DomLayout? DomLayout { get; init; }
    public DomSpacing? DomSpacing { get; init; }
    public DomVisibility? DomVisibility { get; init; }
    public BehaviorTracking? Tracking { get; init; }
    public BehaviorNavigation? Navigation { get; init; }
    public BehaviorInteraction? Interaction { get; init; }
}
