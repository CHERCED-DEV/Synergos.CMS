using System.Text.Json;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="TreeViewResolutor"/>: el TEXTO <c>treeJson</c> llega como el ÁRBOL <c>tree</c>
/// que el elemento lee, a cualquier profundidad — con el alias el árbol salía vacío (D1) — y el
/// <c>ariaLabel</c> del editor como su nombre accesible.
/// </summary>
public sealed class TreeViewResolutorTests
{
    private const string Arbol =
        """[{"label":"Productos","children":[{"label":"Hogar","children":[{"label":"Cocina","href":"/cocina"}]},{"label":"Jardín"}]},{"label":"Servicios"}]""";

    private readonly ILogger<TreeViewResolutor> _log = Substitute.For<ILogger<TreeViewResolutor>>();

    private TreeViewResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(("expandAll", false))).Props));
    }

    [Fact]
    public void El_arbol_viaja_con_sus_ramas_y_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("treeJson", Arbol),
            ("expandAll", true),
            ("ariaLabel", "Catálogo"))).Props);

        Assert.Equal(new[] { "tree", "expandAll", "label" }, cable.Keys);
        var tree = (JsonElement)cable["tree"]!;
        Assert.Equal(2, tree.GetArrayLength());
        var cocina = tree[0].GetProperty("children")[0].GetProperty("children")[0];
        Assert.Equal("Cocina", cocina.GetProperty("label").GetString());
        Assert.False(cocina.TryGetProperty("href", out _));
        Assert.False(tree[1].TryGetProperty("children", out _));
        Assert.True(((JsonElement)cable["expandAll"]!).GetBoolean());
        Assert.Equal("Catálogo", cable["label"]?.ToString());
    }

    [Fact]
    public void Un_nodo_sin_rotulo_o_unos_hijos_que_no_son_lista_no_viajan_y_se_anotan()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(("treeJson",
            """[{"label":"Raíz","children":[{"href":"/sin-rotulo"},{"label":"Hoja","children":"no es lista"},{"label":"Otra","children":null}]}]"""))).Props;

        var raiz = Assert.Single(props.Tree!);
        Assert.Equal(new[] { "Hoja", "Otra" }, raiz.Children!.Select(n => n.Label));
        Assert.All(raiz.Children!, n => Assert.Null(n.Children));
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("treeJson", Arbol), ("expandAll", true));

        // Por el cable y no por Equals: un record compara sus listas por referencia.
        Assert.Equal(
            JsonSerializer.Serialize(Resolutor().Resolver(elemento).Props, SolicitudSynHost.Cable),
            JsonSerializer.Serialize(Resolutor().Resolver(elemento).Props, SolicitudSynHost.Cable));
    }
}
