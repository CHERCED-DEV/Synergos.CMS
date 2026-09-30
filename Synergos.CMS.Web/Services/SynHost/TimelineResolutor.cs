using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTimeline</c> → <see cref="TimelineProps"/>. Parsea en el servidor el JSON de hitos
/// que el editor escribe en <c>eventsJson</c>.
/// </summary>
/// <remarks>
/// El texto de cada hito sale de <c>description</c>, que es lo que documenta el ElementType, o de
/// <c>body</c>, que es como lo llama el elemento. Un hito sin fecha, título ni texto no viaja y se
/// anota: el elemento lo descartaría igual.
/// </remarks>
public sealed class TimelineResolutor : IResolutorSynHost<TimelineProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<TimelineResolutor> _log;

    public TimelineResolutor(IPublishedValueFallback fallback, ILogger<TimelineResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<TimelineProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<TimelineProps>(new TimelineProps(
            Events: Hitos(editor, editor.Texto("eventsJson"))));
    }

    private static IReadOnlyList<TimelineEntry>? Hitos(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("eventsJson", json);
        if (lista is null)
        {
            return null;
        }

        var hitos = new List<TimelineEntry>();
        foreach (var entrada in lista)
        {
            var hito = new TimelineEntry(
                LectorDelEditor.Cadena(entrada, "date"),
                LectorDelEditor.Cadena(entrada, "title"),
                LectorDelEditor.Cadena(entrada, "description") ?? LectorDelEditor.Cadena(entrada, "body"));

            if (hito is { Date: null, Title: null, Body: null })
            {
                editor.NoEsValido("eventsJson", entrada.GetRawText(), "un hito con date, title o description");
                continue;
            }

            hitos.Add(hito);
        }

        return hitos.Count > 0 ? hitos : null;
    }
}
