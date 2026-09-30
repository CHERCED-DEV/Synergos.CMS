using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTabs</c> → <see cref="TabsProps"/>. Parsea en el servidor el JSON de pestañas que
/// el editor escribe en <c>tabsJson</c>.
/// </summary>
/// <remarks>
/// Una pestaña sin <c>label</c> no viaja y se anota: el elemento la descartaría igual (no tiene
/// qué poner en el botón), y así lo que el editor escribió mal no se pierde callado.
/// </remarks>
public sealed class TabsResolutor : IResolutorSynHost<TabsProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<TabsResolutor> _log;

    public TabsResolutor(IPublishedValueFallback fallback, ILogger<TabsResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<TabsProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<TabsProps>(new TabsProps(
            Tabs: Pestanas(editor, editor.Texto("tabsJson")),
            InitialTab: editor.Texto("initialTab")));
    }

    private static IReadOnlyList<TabsItem>? Pestanas(LectorDelEditor editor, string? json)
    {
        var lista = editor.ListaJson("tabsJson", json);
        if (lista is null)
        {
            return null;
        }

        var pestanas = new List<TabsItem>();
        foreach (var entrada in lista)
        {
            var rotulo = LectorDelEditor.Cadena(entrada, "label");
            if (rotulo is null)
            {
                editor.NoEsValido("tabsJson", entrada.GetRawText(), "una pestaña con label");
                continue;
            }

            pestanas.Add(new TabsItem(
                rotulo,
                LectorDelEditor.Cadena(entrada, "id"),
                LectorDelEditor.Cadena(entrada, "content")));
        }

        return pestanas.Count > 0 ? pestanas : null;
    }
}
