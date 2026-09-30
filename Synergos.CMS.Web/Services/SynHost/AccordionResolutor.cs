using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynAccordion</c> → <see cref="AccordionProps"/>. Parsea en el servidor el JSON de
/// secciones que el editor escribe en <c>itemsJson</c>.
/// </summary>
/// <remarks>
/// Acepta lo que documenta el ElementType (<c>title</c>/<c>content</c>) y el nombre que el elemento
/// lee (<c>body</c>), y sale con el del elemento (<c>body</c>). Una sección sin título no viaja y se
/// anota: el elemento la descartaría igual, y así no se pierde callada.
/// </remarks>
public sealed class AccordionResolutor : IResolutorSynHost<AccordionProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<AccordionResolutor> _log;

    public AccordionResolutor(IPublishedValueFallback fallback, ILogger<AccordionResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<AccordionProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<AccordionProps>(new AccordionProps(
            Items: Secciones(editor, editor.Texto("itemsJson")),
            // Sólo el ENCENDIDO viaja: una sección abierta a la vez es el default del elemento.
            AllowMultiple: editor.Interruptor("allowMultiple") ? true : null));
    }

    private static IReadOnlyList<AccordionSection>? Secciones(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("itemsJson", json);
        if (lista is null)
        {
            return null;
        }

        var secciones = new List<AccordionSection>();
        foreach (var entrada in lista)
        {
            var titulo = LectorDelEditor.Cadena(entrada, "title");
            if (titulo is null)
            {
                editor.NoEsValido("itemsJson", entrada.GetRawText(), "una sección con title");
                continue;
            }

            secciones.Add(new AccordionSection(
                titulo,
                LectorDelEditor.Cadena(entrada, "content") ?? LectorDelEditor.Cadena(entrada, "body")));
        }

        return secciones.Count > 0 ? secciones : null;
    }
}
