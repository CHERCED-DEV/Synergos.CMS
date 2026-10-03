using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynStepper</c> → <see cref="StepperProps"/>. Parsea en el servidor el JSON de pasos y
/// lleva el paso activo como número.
/// </summary>
/// <remarks>
/// El texto de cada paso sale de <c>label</c>, que es lo que documenta el ElementType, o de
/// <c>title</c>, que es como lo llama el elemento. Un paso sin texto no viaja y se anota: el
/// elemento lo descartaría igual, y así lo que el editor escribió mal no se pierde callado.
/// </remarks>
public sealed class StepperResolutor : IResolutorSynHost<StepperProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<StepperResolutor> _log;

    public StepperResolutor(IPublishedValueFallback fallback, ILogger<StepperResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<StepperProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<StepperProps>(new StepperProps(
            Steps: Pasos(editor, editor.Texto("stepsJson")),
            CurrentStep: editor.Entero("currentStep")));
    }

    private static IReadOnlyList<StepperItem>? Pasos(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("stepsJson", json);
        if (lista is null)
        {
            return null;
        }

        var pasos = new List<StepperItem>();
        foreach (var entrada in lista)
        {
            var titulo = LectorDelEditor.Cadena(entrada, "label") ?? LectorDelEditor.Cadena(entrada, "title");
            if (titulo is null)
            {
                editor.NoEsValido("stepsJson", entrada.GetRawText(), "un paso con label");
                continue;
            }

            pasos.Add(new StepperItem(
                titulo,
                LectorDelEditor.Cadena(entrada, "description"),
                LectorDelEditor.Cadena(entrada, "id")));
        }

        return pasos.Count > 0 ? pasos : null;
    }
}
