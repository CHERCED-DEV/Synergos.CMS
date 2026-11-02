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
            if (pagina.Seleccion is not null)
            {
                Assert.Contains(pagina.Seleccion, ValoresDe("DTSelectSeleccionDelCatalogo"));
            }
        }

        var repetidas = DevTiendaPorPaginas.Paginas
            .GroupBy(p => (p.Bloque, p.Vista, p.Seleccion))
            .Where(g => g.Count() > 1)
            .Select(g => g.Key);
        Assert.Empty(repetidas);
    }

    [Fact]
    public void Con_el_inicio_y_el_panel_que_ya_existen_hay_una_pagina_por_vista()
    {
        var porBloque = DevTiendaPorPaginas.Paginas.Where(p => p.Seleccion is null).ToLookup(p => p.Bloque, p => p.Vista);

        Assert.Equal(
            ValoresDe("DTSelectVistaDeLaTienda").Where(v => v != "inicio").Order(),
            porBloque["elementSynStorefront"].Order());
        Assert.Equal(
            ValoresDe("DTSelectVistaDeLaConsola").Where(v => v != "panel").Order(),
            porBloque["elementSynSeller"].Order());
    }

    /// <summary>
    /// I5: la página de una selección («Ofertas») va en el menú del sitio con su título; las de una vista son partes de
    /// la app, fuera del menú.
    /// </summary>
    [Fact]
    public void Las_paginas_de_una_seleccion_van_en_el_menu_y_las_de_una_vista_no()
    {
        Assert.All(DevTiendaPorPaginas.Paginas, p => Assert.Equal(p.Seleccion is not null, p.EnElMenu));
        Assert.Contains(DevTiendaPorPaginas.Paginas, p => p.Nombre == "Ofertas" && p.Seleccion == "ofertas");
    }

    /// <summary>
    /// Las ofertas de muestra son productos del contenido del repo, y su precio de antes es MAYOR que su precio base: si
    /// no, la tienda no las mostraría como ofertas y la página «Ofertas» de la demo saldría vacía.
    /// </summary>
    [Fact]
    public void Las_ofertas_de_muestra_son_productos_del_repo_con_un_precio_de_antes_mayor()
    {
        var contenido = Path.Combine(RaizDelRepo(), "Synergos.CMS.Web", "uSync", "v9", "Content");
        var precios = Directory.EnumerateFiles(contenido, "*.config")
            .Select(File.ReadAllText)
            .Where(xml => xml.Contains("<ContentType>productPage</ContentType>", StringComparison.Ordinal))
            .Select(xml => (Sku: Valor(xml, "productSku"), Precio: Valor(xml, "productPriceBase")))
            .Where(p => p.Sku is not null)
            .ToDictionary(p => p.Sku!, p => decimal.Parse(p.Precio!, System.Globalization.CultureInfo.InvariantCulture));

        foreach (var (sku, antes) in DevTiendaPorPaginas.OfertasDeMuestra)
        {
            Assert.True(precios.TryGetValue(sku, out var precio), $"{sku} no está en el contenido del repo");
            Assert.True(decimal.Parse(antes, System.Globalization.CultureInfo.InvariantCulture) > precio, $"{sku}: {antes} no es mayor que {precio}");
        }
    }

    private static string? Valor(string xml, string alias)
    {
        var m = Regex.Match(xml, $@"<{alias}>\s*<Value[^>]*><!\[CDATA\[(.*?)\]\]></Value>", RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : null;
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
