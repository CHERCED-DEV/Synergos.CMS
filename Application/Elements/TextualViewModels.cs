using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class HeadingViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Heading";
    public override string BlockClass => "sg-element--text-heading";
    public override string Alias      => "elementTextHeading";

    public ContentHeadingModel? Heading { get; init; }
}

public sealed class ParagraphViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Paragraph";
    public override string BlockClass => "sg-element--text-paragraph";
    public override string Alias      => "elementTextParagraph";

    public ContentTextModel? Text { get; init; }
}

public sealed class RichTextElementViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/RichText";
    public override string BlockClass => "sg-element--text-richtext";
    public override string Alias      => "elementTextRichText";

    public ContentTextModel? Text { get; init; }
}

public sealed class EyebrowViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Eyebrow";
    public override string BlockClass => "sg-element--text-eyebrow";
    public override string Alias      => "elementTextEyebrow";

    public ContentTextModel? Text { get; init; }
}

public sealed class QuoteViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Quote";
    public override string BlockClass => "sg-element--text-quote";
    public override string Alias      => "elementTextQuote";

    public ContentTextModel? Text { get; init; }
}

public sealed class LabelViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/Label";
    public override string BlockClass => "sg-element--text-label";
    public override string Alias      => "elementTextLabel";

    public ContentTextModel? Text { get; init; }
}

public sealed class TextBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/TextBlock";
    public override string BlockClass => "sg-element--text-block";
    public override string Alias      => "elementTextBlock";

    /// <summary>Title + body copy rendered as a self-contained text block.</summary>
    public ContentTextModel? Text { get; init; }
}

public sealed class CodeBlockViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/CodeBlock";
    public override string BlockClass => "sg-element--text-code-block";
    public override string Alias      => "elementTextCodeBlock";

    public string? Language { get; init; }
    public string? Content  { get; init; }
}

public sealed class AttributedQuoteViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Foundation/AttributedQuote";
    public override string BlockClass => "sg-element--text-attributed-quote";
    public override string Alias      => "elementTextAttributedQuote";

    public string? QuoteText   { get; init; }
    public string? QuoteAuthor { get; init; }
    public string? QuoteRole   { get; init; }
}
