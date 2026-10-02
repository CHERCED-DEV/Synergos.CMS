using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynSearchBox</c> → <see cref="SearchBoxProps"/>: el texto de ayuda del editor y si
/// buscar recarga la página con <c>?q</c> para el listado que la acompaña (#196, tanda D).
/// </summary>
public sealed class SearchBoxResolutor : IResolutorSynHost<SearchBoxProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<SearchBoxResolutor> _log;

    public SearchBoxResolutor(IPublishedValueFallback fallback, ILogger<SearchBoxResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<SearchBoxProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);

        return new ElementoResuelto<SearchBoxProps>(new SearchBoxProps(
            Placeholder: editor.Texto("searchPlaceholder"),
            SubmitToPage: editor.Interruptor("submitToPage") ? true : null));
    }
}
