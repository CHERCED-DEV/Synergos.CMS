using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynBreadcrumb</c> → <see cref="BreadcrumbProps"/>. Parsea en el servidor el JSON de
/// pasos que el editor escribe en <c>itemsJson</c>.
/// </summary>
/// <remarks>
/// Acepta lo que documenta el ElementType (<c>url</c>) y el nombre que el elemento lee
/// (<c>href</c>), y sale con el del elemento. Un paso sin texto no viaja y se anota.
/// </remarks>
public sealed class BreadcrumbResolutor : IResolutorSynHost<BreadcrumbProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<BreadcrumbResolutor> _log;

    public BreadcrumbResolutor(IPublishedValueFallback fallback, ILogger<BreadcrumbResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<BreadcrumbProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<BreadcrumbProps>(new BreadcrumbProps(
            Items: Pasos(editor, editor.Texto("itemsJson")),
            // Sólo el ENCENDIDO viaja: sin JSON-LD es el default del elemento.
            IncludeStructuredData: editor.Interruptor("includeStructuredData") ? true : null));
    }

    private static IReadOnlyList<BreadcrumbStep>? Pasos(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("itemsJson", json);
        if (lista is null)
        {
            return null;
        }

        var pasos = new List<BreadcrumbStep>();
        foreach (var entrada in lista)
        {
            var texto = LectorDelEditor.Cadena(entrada, "label");
            if (texto is null)
            {
                editor.NoEsValido("itemsJson", entrada.GetRawText(), "un paso con label");
                continue;
            }

            pasos.Add(new BreadcrumbStep(
                texto,
                LectorDelEditor.Cadena(entrada, "url") ?? LectorDelEditor.Cadena(entrada, "href")));
        }

        return pasos.Count > 0 ? pasos : null;
    }
}
