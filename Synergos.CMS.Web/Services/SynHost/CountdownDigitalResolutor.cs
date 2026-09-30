using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary><c>elementSynCountdownDigital</c> → <see cref="CountdownDigitalProps"/>.</summary>
/// <remarks>
/// La fecha viaja como <c>targetDate</c> validada, el interruptor siempre, y el estilo con el
/// nombre que le da el elemento.
/// </remarks>
public sealed class CountdownDigitalResolutor : IResolutorSynHost<CountdownDigitalProps>
{
    /// <summary>
    /// Los estilos que el DataType (<c>DTSelectCountdownStyle</c>) y el elemento llaman distinto.
    /// Sólo los renombres: el vocabulario de estilos es del elemento.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NombreEnElElemento =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["digits"] = "plain",
        };

    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<CountdownDigitalResolutor> _log;

    public CountdownDigitalResolutor(IPublishedValueFallback fallback, ILogger<CountdownDigitalResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<CountdownDigitalProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        var estilo = editor.Texto("style");

        return new ElementoResuelto<CountdownDigitalProps>(new CountdownDigitalProps(
            TargetDate: editor.FechaIso("endDateTime"),
            ShowLabels: editor.Interruptor("showLabels"),
            Style: estilo is not null && NombreEnElElemento.TryGetValue(estilo, out var nombre) ? nombre : estilo?.ToLowerInvariant()));
    }
}
