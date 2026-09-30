using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynProgressBar</c> → <see cref="ProgressBarProps"/>.</summary>
public sealed class ProgressBarResolutor : IResolutorSynHost<ProgressBarProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<ProgressBarResolutor> _log;

    public ProgressBarResolutor(IPublishedValueFallback fallback, ILogger<ProgressBarResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<ProgressBarProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<ProgressBarProps>(new ProgressBarProps(
            Value: editor.Numero("valueNow"),
            Max: editor.Numero("valueMax"),
            Label: editor.Texto("ariaLabel")));
    }
}
