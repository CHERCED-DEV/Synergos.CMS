using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynRichTooltip</c> → <see cref="RichTooltipProps"/>.</summary>
/// <remarks>
/// El RTE viaja como texto plano en <c>body</c> y el placement del DataType como el lado que el
/// elemento sabe pintar.
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
            Placement: Lado(editor.Texto("placement"))));
    }

    /// <summary>
    /// El lado de un placement del DataType (<c>DTSelectPlacement</c>): «bottom-start» → «bottom».
    /// La alineación la ofrece el DataType y el elemento no la pinta.
    /// </summary>
    private static string? Lado(string? placement)
    {
        if (placement is null)
        {
            return null;
        }

        var guion = placement.IndexOf('-', StringComparison.Ordinal);
        return (guion > 0 ? placement[..guion] : placement).ToLowerInvariant();
    }
}
