using Umbraco.Cms.Core.Models.PublishedContent;
using Synergos.CMS.Application.Elements;
using Synergos.CMS.Application.Mapping.Compositions.Content;
using Synergos.CMS.Application.Mapping.Compositions.Dom;
using Synergos.CMS.Application.Output;
using Synergos.CMS.Domain.Sections;

namespace Synergos.CMS.Application.Mapping.Elements;

public sealed class HeadingMapper(
    ContentHeadingReader heading,
    DomClassReader       cls,
    DomAttributesReader  attrs) : ISectionMapper
{
    public string SupportedAlias => "elementTextHeading";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new HeadingViewModel
    {
        Heading       = heading.Read(e),
        DomClass      = cls.Read(e),
        DomAttributes = attrs.Read(e)
    });
}

public sealed class ParagraphMapper(
    ContentTextReader textReader,
    DomClassReader    cls) : ISectionMapper
{
    public string SupportedAlias => "elementTextParagraph";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new ParagraphViewModel
    {
        Text     = textReader.Read(e),
        DomClass = cls.Read(e)
    });
}

public sealed class RichTextElementMapper(
    ContentTextReader textReader,
    DomSpacingReader  spacing,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextRichText";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new RichTextElementViewModel
    {
        Text       = textReader.Read(e),
        DomSpacing = spacing.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class EyebrowMapper(
    ContentTextReader textReader,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextEyebrow";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new EyebrowViewModel
    {
        Text       = textReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class QuoteMapper(
    ContentTextReader textReader,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextQuote";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new QuoteViewModel
    {
        Text       = textReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class LabelMapper(
    ContentTextReader textReader,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextLabel";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new LabelViewModel
    {
        Text       = textReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class TextBlockMapper(
    ContentTextReader textReader,
    DomClassReader    cls,
    DomVariantReader  variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextBlock";
    public ISection? Map(IPublishedElement e) => new ComponentSection(new TextBlockViewModel
    {
        Text       = textReader.Read(e),
        DomClass   = cls.Read(e),
        DomVariant = variant.Read(e)
    });
}

public sealed class CodeBlockMapper(DomClassReader cls) : ISectionMapper
{
    public string SupportedAlias => "elementTextCodeBlock";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new CodeBlockViewModel
    {
        Language = element.Value<string>("codeLanguage"),
        Content  = element.Value<string>("codeContent"),
        DomClass = cls.Read(element)
    });
}

public sealed class AttributedQuoteMapper(
    DomClassReader   cls,
    DomVariantReader variant) : ISectionMapper
{
    public string SupportedAlias => "elementTextAttributedQuote";
    public ISection? Map(IPublishedElement element) => new ComponentSection(new AttributedQuoteViewModel
    {
        QuoteText   = element.Value<string>("quoteText"),
        QuoteAuthor = element.Value<string>("quoteAuthor"),
        QuoteRole   = element.Value<string>("quoteRole"),
        DomClass    = cls.Read(element),
        DomVariant  = variant.Read(element)
    });
}
