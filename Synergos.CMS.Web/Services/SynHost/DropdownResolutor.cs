using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynDropdown</c> → <see cref="DropdownProps"/>. Parsea en el servidor el JSON que el
/// editor escribe en <c>optionsJson</c>.
/// </summary>
/// <remarks>
/// Acepta lo que documenta el ElementType (<c>value</c>/<c>label</c>) y lo que el elemento sabe
/// pintar (<c>href</c>, o <c>url</c> como alias): una opción con sólo uno de los dos textos usa el
/// mismo para ambos. Un JSON que no parsea o una entrada que no es una opción —un grupo— no viaja y
/// se anota; la página no se cae por lo que el editor escribió.
/// </remarks>
public sealed class DropdownResolutor : IResolutorSynHost<DropdownProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<DropdownResolutor> _log;

    public DropdownResolutor(IPublishedValueFallback fallback, ILogger<DropdownResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<DropdownProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<DropdownProps>(new DropdownProps(
            TriggerLabel: editor.Texto("triggerLabel"),
            Options: Opciones(editor, editor.Texto("optionsJson")),
            SelectedValue: editor.Texto("selectedValue"),
            // Sólo el ENCENDIDO viaja: apagado es el default del elemento, y un bloque que nunca
            // tocó el interruptor debe seguir ese default si algún día cambia.
            Searchable: editor.Interruptor("searchable") ? true : null));
    }

    private static IReadOnlyList<DropdownOption>? Opciones(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("optionsJson", json);
        if (lista is null)
        {
            return null;
        }

        var opciones = new List<DropdownOption>();
        foreach (var entrada in lista)
        {
            var value = LectorDelEditor.Cadena(entrada, "value");
            var label = LectorDelEditor.Cadena(entrada, "label");
            if (value is null && label is null)
            {
                editor.NoEsValido("optionsJson", entrada.GetRawText(), "una opción con value o label");
                continue;
            }

            opciones.Add(new DropdownOption(
                value ?? label!,
                label ?? value!,
                LectorDelEditor.Cadena(entrada, "href") ?? LectorDelEditor.Cadena(entrada, "url")));
        }

        return opciones.Count > 0 ? opciones : null;
    }
}
