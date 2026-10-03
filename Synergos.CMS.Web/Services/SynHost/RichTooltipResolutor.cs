using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynRichTooltip</c> → <see cref="RichTooltipProps"/>.</summary>
/// <remarks>
/// El contenido viaja en <c>body</c> como texto plano, y el placement tal cual: el DataType ya no
/// ofrece las alineaciones (-start/-end) que ningún elemento pinta, y el contenido ya no es un RTE
/// cuyo formato se perdía (#192, caso 16). Lo que un contenido viejo guarde con etiquetas se limpia.
/// </remarks>
public sealed class RichTooltipResolutor : IResolutorSynHost<RichTooltipProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<RichTooltipResolutor> _log;

    public RichTooltipResolutor(IPublishedValueFallback fallback, ILogger<RichTooltipResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<RichTooltipProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<RichTooltipProps>(new RichTooltipProps(
            TriggerText: editor.Texto("triggerText"),
            Body: editor.TextoPlano("tooltipContent"),
            Placement: editor.Texto("placement")?.ToLowerInvariant()));
    }
}
