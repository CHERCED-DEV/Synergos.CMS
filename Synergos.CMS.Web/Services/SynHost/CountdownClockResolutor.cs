using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynCountdownClock</c> → <see cref="CountdownClockProps"/>.</summary>
/// <remarks>
/// La fecha viaja como <c>targetDate</c>, validada como ISO 8601. El ElementType ya no ofrece
/// <c>labelFormat</c> (#192, caso 18; ver el record).
/// </remarks>
public sealed class CountdownClockResolutor : IResolutorSynHost<CountdownClockProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<CountdownClockResolutor> _log;

    public CountdownClockResolutor(IPublishedValueFallback fallback, ILogger<CountdownClockResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<CountdownClockProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<CountdownClockProps>(new CountdownClockProps(
            TargetDate: editor.FechaIso("endDateTime")));
    }
}
