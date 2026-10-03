using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTourGuide</c> → <see cref="TourGuideProps"/>. Parsea en el servidor el JSON de
/// pasos que el editor escribe en <c>stepsJson</c>.
/// </summary>
/// <remarks>
/// Acepta lo que documenta el ElementType (<c>selector</c>, <c>title</c>, <c>content</c>) y los
/// nombres del elemento (<c>target</c>, <c>body</c>). Un paso sin título ni texto no viaja y se
/// anota: el elemento lo descartaría igual (no tiene qué mostrar). Sin <c>selector</c> es válido:
/// un paso de bienvenida centrado, sin nada que iluminar.
/// </remarks>
public sealed class TourGuideResolutor : IResolutorSynHost<TourGuideProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<TourGuideResolutor> _log;

    public TourGuideResolutor(IPublishedValueFallback fallback, ILogger<TourGuideResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<TourGuideProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<TourGuideProps>(new TourGuideProps(
            Steps: Pasos(editor, editor.Texto("stepsJson")),
            AutoStart: editor.Interruptor("autoStart") ? true : null));
    }

    private static IReadOnlyList<TourGuideStep>? Pasos(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("stepsJson", json);
        if (lista is null)
        {
            return null;
        }

        var pasos = new List<TourGuideStep>();
        foreach (var entrada in lista)
        {
            var paso = new TourGuideStep(
                LectorDelEditor.Cadena(entrada, "selector") ?? LectorDelEditor.Cadena(entrada, "target"),
                LectorDelEditor.Cadena(entrada, "title"),
                LectorDelEditor.Cadena(entrada, "content") ?? LectorDelEditor.Cadena(entrada, "body"),
                LectorDelEditor.Cadena(entrada, "placement")?.ToLowerInvariant());

            if (paso is { Title: null, Body: null })
            {
                editor.NoEsValido("stepsJson", entrada.GetRawText(), "un paso con title o content");
                continue;
            }

            pasos.Add(paso);
        }

        return pasos.Count > 0 ? pasos : null;
    }
}
