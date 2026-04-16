using Synergos.CMS.Application.Components;
using Synergos.CMS.Domain.Compositions.Content;

namespace Synergos.CMS.Application.Elements;

public sealed class ContactInfoViewModel : BaseComponentViewModel
{
    public override string ViewName   => "Partials/Ssr/Components/ContactInfo";
    public override string BlockClass => "sg-element--corp-contact-info";
    public override string Alias      => "elementCorpContactInfo";

    public ContentTextModel?     Text     { get; init; }
    public ContentCtaModel?      Cta      { get; init; }
    public ContentLocationModel? Location { get; init; }
}
