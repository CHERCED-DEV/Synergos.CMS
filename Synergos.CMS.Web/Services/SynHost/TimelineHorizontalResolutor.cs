using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTimelineHorizontal</c> → <see cref="TimelineHorizontalProps"/>. Parsea en el servidor
/// el JSON de ítems que el editor escribe en <c>eventsJson</c>.
/// </summary>
/// <remarks>
/// La hora sale de <c>time</c> o, para el contenido ya autorado, de <c>date</c> (la demo de
/// <c>/eventos/</c> la escribe así), y viaja como <c>time</c>. Un ítem sin hora o sin título no viaja
/// y se anota: el elemento lo descartaría igual, pero callado.
/// </remarks>
public sealed class TimelineHorizontalResolutor : IResolutorSynHost<TimelineHorizontalProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<TimelineHorizontalResolutor> _log;

    public TimelineHorizontalResolutor(IPublishedValueFallback fallback, ILogger<TimelineHorizontalResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<TimelineHorizontalProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<TimelineHorizontalProps>(new TimelineHorizontalProps(
            Items: Items(editor, editor.Texto("eventsJson")),
            SnapEnabled: editor.Interruptor("snapEnabled")));
    }

    private static IReadOnlyList<TimelineHorizontalItem>? Items(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("eventsJson", json);
        if (lista is null)
        {
            return null;
        }

        var items = new List<TimelineHorizontalItem>();
        foreach (var entrada in lista)
        {
            var hora = LectorDelEditor.Cadena(entrada, "time") ?? LectorDelEditor.Cadena(entrada, "date");
            var titulo = LectorDelEditor.Cadena(entrada, "title");
            if (hora is null || titulo is null)
            {
                editor.NoEsValido("eventsJson", entrada.GetRawText(), "un ítem con time (o date) y title");
                continue;
            }

            items.Add(new TimelineHorizontalItem(
                hora,
                titulo,
                LectorDelEditor.Cadena(entrada, "track"),
                LectorDelEditor.Cadena(entrada, "description")));
        }

        return items.Count > 0 ? items : null;
    }
}
