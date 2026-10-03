using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Synergos.CMS.Web.Filters;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Filters;

/// <summary>
/// El sitio y la cultura de una página que pinta un controlador MVC (#190, #188):
/// <see cref="SitioDeLaPeticion"/> y <see cref="CulturaDelSitioAttribute"/>.
/// </summary>
/// <remarks>
/// Medido en vivo antes del arreglo: <c>/blog/tag/*</c> y <c>/error/404</c> salían con el bridge en
/// <c>en-US</c> y <c>&lt;html lang="en"&gt;</c> en un sitio <c>es-CO</c>, en los tres hosts probados,
/// y la página de error de <c>blogs.localhost</c> era la del primer sitio del árbol.
/// </remarks>
public sealed class CulturaDelSitioTests
{
    private const int Blogs = 1524;

    private static readonly Domain[] Dominios =
    {
        new(1, "blogs.localhost:5206", Blogs, "es-CO", false, 0),
        new(2, "blogs-en.localhost:5206", Blogs, "en-US", false, 1),
    };

    private static IPublishedContent SiteRoot()
    {
        var tipo = Substitute.For<IPublishedContentType>();
        tipo.Alias.Returns("siteRoot");
        var nodo = Substitute.For<IPublishedContent>();
        nodo.Id.Returns(Blogs);
        nodo.ContentType.Returns(tipo);
        nodo.Parent.Returns((IPublishedContent?)null);
        return nodo;
    }

    private static (SitioDeLaPeticion Sitio, IPublishedContent Raiz) Peticion(string url)
    {
        var raiz = SiteRoot();
        var contenido = Substitute.For<IPublishedContentCache>();
        contenido.GetById(Blogs).Returns(raiz);

        var dominios = Substitute.For<IDomainCache>();
        dominios.GetAll(Arg.Any<bool>()).Returns(Dominios);
        dominios.DefaultCulture.Returns("es-CO");

        var contexto = Substitute.For<IUmbracoContext>();
        contexto.Domains.Returns(dominios);
        contexto.Content.Returns(contenido);
        contexto.CleanedUmbracoUrl.Returns(new Uri(url));

        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!)
            .Returns(x => { x[0] = contexto; return true; });

        return (new SitioDeLaPeticion(accesor), raiz);
    }

    [Fact]
    public void El_dominio_de_la_peticion_da_su_sitio_y_su_cultura()
    {
        var (sitio, raiz) = Peticion("http://blogs-en.localhost:5206/blog/tag/editor");

        var resuelto = sitio.Resolver();

        Assert.Same(raiz, resuelto.SiteRoot);
        Assert.Equal("en-US", resuelto.Cultura);
    }

    [Fact]
    public void Sin_dominio_que_case_no_hay_sitio_y_la_cultura_es_la_por_defecto_de_Umbraco()
    {
        var (sitio, _) = Peticion("http://localhost:5206/blog/tag/editor");

        var resuelto = sitio.Resolver();

        Assert.Null(resuelto.SiteRoot);
        Assert.Equal("es-CO", resuelto.Cultura);
    }

    [Fact]
    public void Sin_contexto_de_Umbraco_no_se_sabe_nada()
    {
        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(false);

        Assert.Equal(SitioResuelto.Ninguno, new SitioDeLaPeticion(accesor).Resolver());
    }

    [Theory]
    [InlineData("http://localhost:5206/blog/tag/editor", "es-CO")]
    [InlineData("http://blogs.localhost:5206/blog/tag/editor", "es-CO")]
    [InlineData("http://blogs-en.localhost:5206/blog/tag/editor", "en-US")]
    public async Task La_pagina_se_pinta_en_la_cultura_de_su_sitio_y_no_en_la_de_ASP_NET(string url, string esperada)
    {
        var (sitio, _) = Peticion(url);
        var filtro = (IAsyncResourceFilter)new CulturaDelSitioAttribute().CreateInstance(
            new ServiceCollection().AddSingleton(sitio).BuildServiceProvider());
        var contexto = new ResourceExecutingContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(),
            new List<IValueProviderFactory>());

        // Lo que deja ASP.NET en una ruta que Umbraco no rutea.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        string? enLaVista = null, enLosFormatos = null;

        await filtro.OnResourceExecutionAsync(contexto, () =>
        {
            enLaVista = CultureInfo.CurrentUICulture.Name;
            enLosFormatos = CultureInfo.CurrentCulture.Name;
            return Task.FromResult(new ResourceExecutedContext(contexto, contexto.Filters));
        });

        Assert.Equal(esperada, enLaVista);
        Assert.Equal(esperada, enLosFormatos);
    }

    /// <summary>
    /// Las páginas que pintan los controladores MVC (no las rutea Umbraco) declaran la cultura de
    /// su sitio. La línea base —vigilada en los dos sentidos— quedó vacía (#193).
    /// </summary>
    /// <remarks>
    /// <para>Se descubren por la FUENTE: un controlador de <c>Controllers/</c> que devuelve una vista
    /// (<c>View(…)</c> o <c>new ViewResult</c>). Los <c>RenderController</c> los rutea Umbraco y
    /// traen la cultura de su dominio; no entran.</para>
    /// <para><b>La línea base era deuda medida</b>: <c>AccountController</c> y <c>AdminController</c>
    /// salían <c>lang="en"</c>. Se pagó en #193, cuando sus claves pasaron a existir en el
    /// diccionario y el login de un sitio es-CO empezó a decir «Sign in» (medido en vivo). El miedo
    /// que la tenía aparte —que la cultura cambie cómo se leen sus formularios— se midió: sus POST
    /// sólo enlazan texto, booleanos y GUID, y los números y fechas de Admin llegan por query string,
    /// que ASP.NET lee en cultura invariante. Sus vistas escriben fechas con formato explícito.</para>
    /// </remarks>
    [Fact]
    public void Las_paginas_de_los_controladores_MVC_se_pintan_en_la_cultura_de_su_sitio()
    {
        var lineaBase = new Dictionary<string, string>(StringComparer.Ordinal);

        var controladores = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Controllers");
        var pintan = Directory.EnumerateFiles(controladores, "*.cs")
            .Select(f => (Nombre: Path.GetFileNameWithoutExtension(f), Texto: File.ReadAllText(f)))
            .Where(c => Regex.IsMatch(c.Texto, @"\bView\(|new\s+ViewResult\b")
                        && !Regex.IsMatch(c.Texto, @":\s*RenderController\b"))
            .ToList();
        Assert.True(pintan.Count >= 4, $"Se encontraron {pintan.Count} controladores que pintan: el descubrimiento está roto.");

        // El atributo se mira por REFLEXIÓN, no por texto: escrito con su espacio de nombres
        // (`[Synergos.CMS.Web.Filters.CulturaDelSitio]`) una regex no lo veía, y la línea base
        // dejaba de vigilarse en un sentido (mutado).
        var tipos = typeof(CulturaDelSitioAttribute).Assembly.GetTypes()
            .GroupBy(t => t.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var sinTipo = pintan.Where(c => !tipos.ContainsKey(c.Nombre)).Select(c => c.Nombre).ToList();
        Assert.True(sinTipo.Count == 0,
            "Estos ficheros no se llaman como su controlador y el gate no los puede mirar: "
            + string.Join(", ", sinTipo));

        var conCultura = pintan
            .Where(c => tipos[c.Nombre].GetCustomAttributes(typeof(CulturaDelSitioAttribute), inherit: true).Length > 0)
            .Select(c => c.Nombre)
            .ToHashSet(StringComparer.Ordinal);

        var sinCultura = pintan
            .Where(c => !conCultura.Contains(c.Nombre) && !lineaBase.ContainsKey(c.Nombre))
            .Select(c => c.Nombre)
            .ToList();
        var pagadas = lineaBase.Keys.Where(conCultura.Contains).ToList();
        var fantasmas = lineaBase.Keys.Where(k => pintan.All(c => c.Nombre != k)).ToList();

        Assert.True(sinCultura.Count == 0,
            "Estos controladores pintan una página que Umbraco no rutea y no declaran [CulturaDelSitio]: "
            + "salen en en-US —bridge, lang y fechas— en un sitio es-CO (#190): "
            + string.Join(", ", sinCultura));
        Assert.True(pagadas.Count == 0 && fantasmas.Count == 0,
            "La línea base envejeció: quitá de ella lo que ya declara la cultura ("
            + string.Join(", ", pagadas) + ") y lo que ya no pinta (" + string.Join(", ", fantasmas) + ").");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
