namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Lo que viaja a <c>&lt;synergos-tree-view&gt;</c>.
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra (D1).</b> La vista mandaba <c>treeJson</c> —el TEXTO del
/// TextArea— y el elemento lee <c>tree</c>, un ÁRBOL: el árbol colocado salía con «No hay
/// elementos para mostrar». Sólo <c>expandAll</c> llegaba.</para>
///
/// <para><b>Viaja como árbol tipado</b>: cada nodo lleva lo que documenta el ElementType,
/// <c>label</c> y <c>children</c> (más nodos, a cualquier profundidad). El elemento sabe además
/// leer <c>id</c>, <c>href</c>, <c>icon</c> y <c>expanded</c> por nodo, pero el ElementType no los
/// ofrece: no viajan.</para>
///
/// <para><c>label</c> es el nombre accesible del árbol (<c>role="tree"</c>) y sale de
/// <c>compDomAttributes.ariaLabel</c>: un texto que el editor escribe por instancia y el elemento
/// pinta (regla 3 del contexto de la escala; precedente <c>rating-stars</c>). Sin él, el elemento
/// dice «Árbol de navegación». <c>expandAll</c> sólo viaja ENCENDIDO: apagado es el default.</para>
///
/// <para><b>Sección <c>TreeView</c></b> (ADR 0136): el nombre por defecto, el estado vacío y
/// «Expandir/Contraer {label}» de cada rama; el nombre del nodo viaja como marcador.</para>
/// </remarks>
[ElementoSynHost("tree-view", TipoDeColocable.Pieza, Diccionario = ["TreeView"])]
public sealed record TreeViewProps(
    [property: CampoSynHost(OrigenDelCampo.Contenido)] IReadOnlyList<TreeViewNode>? Tree,
    [property: CampoSynHost(OrigenDelCampo.Decision)] bool? ExpandAll,
    [property: CampoSynHost(OrigenDelCampo.Contenido)] string? Label);

/// <summary>Un nodo del árbol: su rótulo y, si tiene, sus hijos.</summary>
public sealed record TreeViewNode(string Label, IReadOnlyList<TreeViewNode>? Children = null);
