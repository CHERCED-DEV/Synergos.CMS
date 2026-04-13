using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Components;
using Synergos.CMS.Application.Mapping.Compositions.Behavior;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping;

/// <summary>
/// Composition-driven fallback mapper. Applied by SectionMapperDispatcher when
/// no specific ISectionMapper is registered for an element type alias.
///
/// Applies every known composition reader (DOM, Behavior, Content) and surfaces
/// the result as a GenericElementViewModel. The Angular renderer uses the Alias
/// field to select the correct component; only non-null composition properties
/// carry data.
///
/// This satisfies OCP: adding a new element type that only uses existing
/// compositions requires zero C# changes — content editors configure it in
/// Umbraco and the generic fallback handles the JSON output automatically.
/// </summary>
public sealed class GenericElementMapper
{
    // ── DOM readers ───────────────────────────────────────────────────────────
    private readonly DomLayoutReader     _layout;
    private readonly DomSpacingReader    _spacing;
    private readonly DomClassReader      _cls;
    private readonly DomAttributesReader _attrs;
    private readonly DomVariantReader    _variant;
    private readonly DomVisibilityReader _visibility;

    // ── Behavior readers ─────────────────────────────────────────────────────
    private readonly BehaviorTrackingReader    _tracking;
    private readonly BehaviorInteractionReader _interaction;
    private readonly BehaviorNavigationReader  _navigation;

    // ── Content readers ──────────────────────────────────────────────────────
    private readonly ContentHeadingReader _heading;
    private readonly ContentTextReader    _text;
    private readonly ContentMediaReader   _media;
    private readonly ContentCtaReader     _cta;
    private readonly ContentBadgeReader   _badge;
    private readonly ContentDateReader    _date;
    private readonly ContentEmbedReader   _embed;

    public GenericElementMapper(
        DomLayoutReader          layout,
        DomSpacingReader         spacing,
        DomClassReader           cls,
        DomAttributesReader      attrs,
        DomVariantReader         variant,
        DomVisibilityReader      visibility,
        BehaviorTrackingReader   tracking,
        BehaviorInteractionReader interaction,
        BehaviorNavigationReader  navigation,
        ContentHeadingReader      heading,
        ContentTextReader         text,
        ContentMediaReader        media,
        ContentCtaReader          cta,
        ContentBadgeReader        badge,
        ContentDateReader         date,
        ContentEmbedReader        embed)
    {
        _layout      = layout;
        _spacing     = spacing;
        _cls         = cls;
        _attrs       = attrs;
        _variant     = variant;
        _visibility  = visibility;
        _tracking    = tracking;
        _interaction = interaction;
        _navigation  = navigation;
        _heading     = heading;
        _text        = text;
        _media       = media;
        _cta         = cta;
        _badge       = badge;
        _date        = date;
        _embed       = embed;
    }

    /// <summary>
    /// Maps any IPublishedElement using all registered composition readers.
    /// The alias parameter is the ContentType.Alias of the element.
    /// Returns null only when the element itself is somehow empty.
    /// </summary>
    public SectionView? Map(IPublishedElement element)
    {
        var alias = element.ContentType.Alias;

        var vm = new GenericElementViewModel(alias)
        {
            // DOM
            DomLayout     = _layout.Read(element),
            DomSpacing    = _spacing.Read(element),
            DomClass      = _cls.Read(element),
            DomAttributes = _attrs.Read(element),
            DomVariant    = _variant.Read(element),
            DomVisibility = _visibility.Read(element),

            // Behavior
            Tracking    = _tracking.Read(element),
            Interaction = _interaction.Read(element),
            Navigation  = _navigation.Read(element),

            // Content
            Heading = _heading.Read(element),
            Text    = _text.Read(element),
            Media   = _media.Read(element),
            Cta     = _cta.Read(element),
            Badge   = _badge.Read(element),
            Date    = _date.Read(element),
            Embed   = _embed.Read(element),
        };

        var (elementId, cssClasses) = DomAttributeReader.Read(element);
        return new SectionView(new ComponentSection(vm), elementId, cssClasses);
    }
}
