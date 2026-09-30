using System.Text.Json;
using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Web.Services.SynHost;

/// <summary>
/// <c>elementSynTreeView</c> → <see cref="TreeViewProps"/>. Parsea en el servidor el árbol JSON que
/// el editor escribe en <c>treeJson</c>, a cualquier profundidad.
/// </summary>
/// <remarks>
/// Los hijos de cada nodo se leen con la misma <see cref="LectorDelEditor.ListaJson"/> que la raíz,
/// así que un <c>children</c> que no es una lista no viaja y se anota igual que un
/// <c>treeJson</c> roto. Un nodo sin <c>label</c> no viaja (con su rama) y se anota: el elemento
/// lo descartaría igual. La profundidad la acota el parser (64 niveles): más que eso no parsea y
/// se anota como JSON inválido.
/// </remarks>
public sealed class TreeViewResolutor : IResolutorSynHost<TreeViewProps>
{
    private readonly IPublishedValueFallback _fallback;
    private readonly ILogger<TreeViewResolutor> _log;

    public TreeViewResolutor(IPublishedValueFallback fallback, ILogger<TreeViewResolutor> log)
    {
        _fallback = fallback;
        _log = log;
    }

    public ElementoResuelto<TreeViewProps> Resolver(IPublishedElement elemento)
    {
        var editor = new LectorDelEditor(elemento, _fallback, _log);
        return new ElementoResuelto<TreeViewProps>(new TreeViewProps(
            Tree: Nodos(editor, editor.ListaJson("treeJson", editor.Texto("treeJson"))),
            ExpandAll: editor.Interruptor("expandAll") ? true : null,
            Label: editor.Texto("ariaLabel")));
    }

    private static IReadOnlyList<TreeViewNode>? Nodos(LectorDelEditor editor, IReadOnlyList<JsonElement>? lista)
    {
        if (lista is null)
        {
            return null;
        }

        var nodos = new List<TreeViewNode>();
        foreach (var entrada in lista)
        {
            var rotulo = LectorDelEditor.Cadena(entrada, "label");
            if (rotulo is null)
            {
                editor.NoEsValido("treeJson", entrada.GetRawText(), "un nodo con label");
                continue;
            }

            var hijos = entrada.TryGetProperty("children", out var crudo) && crudo.ValueKind != JsonValueKind.Null
                ? Nodos(editor, editor.ListaJson("treeJson", crudo.GetRawText()))
                : null;
            nodos.Add(new TreeViewNode(rotulo, hijos));
        }

        return nodos.Count > 0 ? nodos : null;
    }
}
