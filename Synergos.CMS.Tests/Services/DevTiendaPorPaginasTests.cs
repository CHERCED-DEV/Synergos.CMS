using System.Text.Json;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las páginas que compone <see cref="DevTiendaPorPaginas"/> (014-despiece-tienda, I3) piden vistas que el bloque de
/// su app sabe abrir: las del desplegable de su ElementType en uSync. Una vista que no está ahí se guardaría y el
/// elemento abriría el inicio, callado.
/// </summary>
public sealed class DevTiendaPorPaginasTests
{
    private static readonly Dictionary<string, string> DesplegableDe = new(StringComparer.Ordinal)
    {
        ["elementSynStorefront"] = "DTSelectVistaDeLaTienda",
        ["elementSynSeller"] = "DTSelectVistaDeLaConsola",
    };

    [Fact]
    public void Cada_pagina_pide_una_vista_del_desplegable_de_su_bloque_y_ninguna_se_repite()
    {
        foreach (var pagina in DevTiendaPorPaginas.Paginas)
        {
            Assert.True(DesplegableDe.TryGetValue(pagina.Bloque, out var desplegable), $"{pagina.Nombre}: {pagina.Bloque} no tiene vista");
            Assert.Contains(pagina.Vista, ValoresDe(desplegable));
        }

        var repetidas = DevTiendaPorPaginas.Paginas.GroupBy(p => (p.Bloque, p.Vista)).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(repetidas);
    }

    [Fact]
    public void Con_el_inicio_y_el_panel_que_ya_existen_hay_una_pagina_por_vista()
    {
        var porBloque = DevTiendaPorPaginas.Paginas.ToLookup(p => p.Bloque, p => p.Vista);

        Assert.Equal(
            ValoresDe("DTSelectVistaDeLaTienda").Where(v => v != "inicio").Order(),
            porBloque["elementSynStorefront"].Order());
        Assert.Equal(
            ValoresDe("DTSelectVistaDeLaConsola").Where(v => v != "panel").Order(),
            porBloque["elementSynSeller"].Order());
    }

    private static IReadOnlyList<string> ValoresDe(string desplegable)
    {
        var xml = File.ReadAllText(Path.Combine(RaizDelRepo(), "Synergos.CMS.Web", "uSync", "v9", "DataTypes", $"{desplegable}.config"));
        var config = Regex.Match(xml, @"<Config><!\[CDATA\[(.*?)\]\]></Config>", RegexOptions.Singleline).Groups[1].Value;
        using var json = JsonDocument.Parse(config);
        return json.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("value").GetString()!).ToList();
    }

    private static string RaizDelRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Synergos.CMS.Web")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("No encuentro la raíz del repo del CMS.");
    }
}
