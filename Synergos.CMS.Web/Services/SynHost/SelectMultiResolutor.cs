using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynSelectMulti</c> → <see cref="SelectMultiProps"/>. Parsea en el servidor el JSON
/// que el editor escribe en <c>optionsJson</c>.
/// </summary>
/// <remarks>
/// Una opción con sólo uno de los dos textos usa el mismo para ambos, como hace el elemento. Un
/// JSON que no parsea o una entrada sin <c>value</c> ni <c>label</c> no viaja y se anota; la
/// página no se cae por lo que el editor escribió.
/// </remarks>
public sealed class SelectMultiResolutor : IResolutorSynHost<SelectMultiProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<SelectMultiResolutor> _log;

    public SelectMultiResolutor(IPublishedValueFallback fallback, ILogger<SelectMultiResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<SelectMultiProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<SelectMultiProps>(new SelectMultiProps(
            Label: editor.Texto("label"),
            Options: Opciones(editor, editor.Texto("optionsJson")),
            MaxSelections: editor.Entero("maxSelections")));
    }

    private static IReadOnlyList<SelectMultiItem>? Opciones(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("optionsJson", json);
        if (lista is null)
        {
            return null;
        }

        var opciones = new List<SelectMultiItem>();
        foreach (var entrada in lista)
        {
            var value = LectorDelEditor.Cadena(entrada, "value");
            var label = LectorDelEditor.Cadena(entrada, "label");
            if (value is null && label is null)
            {
                editor.NoEsValido("optionsJson", entrada.GetRawText(), "una opción con value o label");
                continue;
            }

            opciones.Add(new SelectMultiItem(value ?? label!, label ?? value!));
        }

        return opciones.Count > 0 ? opciones : null;
    }
}
