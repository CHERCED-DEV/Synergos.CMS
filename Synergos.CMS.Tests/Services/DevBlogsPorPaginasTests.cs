using System.Text.RegularExpressions;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Las páginas que compone <see cref="DevBlogsPorPaginas"/> (012-blogs-social, I3) piden vistas que el bloque de la red
/// sabe abrir: las del desplegable <c>DTSelectVistaDeBlogs</c> en uSync. Una vista que no está ahí se guardaría y el
/// elemento abriría el inicio, callado.
/// </summary>
public sealed class DevBlogsPorPaginasTests
{
    /// <summary>Ninguna vista se repite entre las páginas que no la acotan; una etiqueta es una selección de Explorar.</summary>
    [Fact]
    public void Cada_pagina_pide_una_vista_del_desplegable_y_ninguna_se_repite()
    {
        var valores = ValoresDe("DTSelectVistaDeBlogs");

        Assert.All(DevBlogsPorPaginas.Paginas, p => Assert.Contains(p.Vista, valores));
        Assert.Empty(DePorVista().GroupBy(p => p.Vista).Where(g => g.Count() > 1).Select(g => g.Key));
        Assert.Empty(DevBlogsPorPaginas.Paginas.GroupBy(p => p.Nombre).Where(g => g.Count() > 1).Select(g => g.Key));
    }

    [Fact]
    public void Con_el_inicio_que_ya_existe_hay_una_pagina_por_vista()
    {
        Assert.Equal(
            ValoresDe("DTSelectVistaDeBlogs").Where(v => v != "inicio").Order(),
            DePorVista().Select(p => p.Vista).Order());
    }

    /// <summary>
    /// Una página que acota su vista (I6, «#arquitectura») cuelga de la página de esa vista, que se crea antes, y lleva
    /// su título: sin él, la página de una etiqueta se llamaría «Explorar».
    /// </summary>
    [Fact]
    public void Una_pagina_por_etiqueta_cuelga_de_la_de_su_vista_y_lleva_su_titulo()
    {
        var paginas = DevBlogsPorPaginas.Paginas.ToList();
        var acotadas = paginas.Where(p => p.Seleccion is not null).ToList();

        Assert.NotEmpty(acotadas);
        Assert.All(acotadas, p =>
        {
            var deSuVista = paginas.FindIndex(q => q.Seleccion is null && q.Vista == p.Vista);
            Assert.True(deSuVista >= 0, $"«{p.Nombre}» acota {p.Vista}, que no tiene página.");
            Assert.Equal(paginas[deSuVista].Nombre, p.Bajo);
            Assert.True(deSuVista < paginas.IndexOf(p), $"«{p.Nombre}» se crearía antes que la página de la que cuelga.");
            Assert.False(string.IsNullOrWhiteSpace(p.Titulo));
        });
    }

    private static IEnumerable<DevBlogsPorPaginas.PaginaDeUnaVista> DePorVista()
        => DevBlogsPorPaginas.Paginas.Where(p => p.Seleccion is null);

    /// <summary>La raíz bajo la que cuelgan es la Blogs del repo, y coloca la app sin vista: es el inicio.</summary>
    [Fact]
    public void Cuelgan_de_la_raiz_de_Blogs_del_repo_que_coloca_la_red()
    {
        var raiz = Directory.EnumerateFiles(Path.Combine(RaizDelRepo(), "Synergos.CMS.Web", "uSync", "v9", "Content"), "*.config")
            .Select(File.ReadAllText)
            .Single(xml => xml.Contains($"<Content Key=\"{DevBlogsPorPaginas.Blogs}\"", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("<ContentType>siteRoot</ContentType>", raiz, StringComparison.Ordinal);
        Assert.Contains("22165b90-336d-44a2-9036-a01e79cf1201", raiz, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Los valores del desplegable, leídos del XML de uSync del repo.</summary>
    private static IReadOnlyList<string> ValoresDe(string desplegable)
    {
        var xml = File.ReadAllText(Path.Combine(RaizDelRepo(), "Synergos.CMS.Web", "uSync", "v9", "DataTypes", $"{desplegable}.config"));
        return Regex.Matches(xml, "\"value\":\\s*\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
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
