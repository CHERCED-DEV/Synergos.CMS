using System.Text;
using System.Xml.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El news-sitemap (012-blogs-social): salía siempre vacío porque Umbraco no crea su contexto para una ruta con
/// extensión (.xml), y la consulta de artículos no veía contenido. La acción lo pide antes de leer y fija la cultura por
/// defecto. Sólo lista lo publicado dentro de la ventana (48 h por defecto), medida con el reloj inyectado.
/// </summary>
public sealed class NewsSitemapControllerTests
{
    // Lejos del reloj real: si la acción volviera a DateTime.UtcNow, la ventana dejaría fuera a los dos artículos.
    private static readonly DateTimeOffset Ahora = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly XNamespace Sitemap = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [Fact]
    public void Pide_el_contexto_de_Umbraco_y_la_cultura_antes_de_leer_los_articulos()
    {
        var (sut, fabrica, consulta, variacion) = Crear(Array.Empty<PostSummary>());

        sut.GetNewsSitemap();

        Received.InOrder(() =>
        {
            fabrica.EnsureUmbracoContext();
            consulta.GetPosts(Arg.Any<BlogQueryRequest>());
        });
        Assert.Equal("es-CO", variacion.VariationContext?.Culture);
    }

    [Fact]
    public void Sale_el_articulo_de_dentro_de_la_ventana_y_no_el_de_fuera_ni_el_que_no_tiene_direccion()
    {
        var (sut, _, _, _) = Crear(new[]
        {
            Articulo("/blogs/de-hoy/", Ahora.UtcDateTime.AddHours(-3)),
            Articulo("/blogs/de-hace-tres-dias/", Ahora.UtcDateTime.AddHours(-72)),
            Articulo("#", Ahora.UtcDateTime.AddHours(-1)),
        });

        var archivo = Assert.IsType<FileContentResult>(sut.GetNewsSitemap());
        var locs = XDocument.Parse(Encoding.UTF8.GetString(archivo.FileContents))
            .Descendants(Sitemap + "loc")
            .Select(l => l.Value);

        Assert.Equal(new[] { "http://blogs.test/blogs/de-hoy/" }, locs);
    }

    private static (NewsSitemapController Sut, IUmbracoContextFactory Fabrica, IBlogQuery Consulta, Variacion Variacion) Crear(
        IReadOnlyList<PostSummary> articulos)
    {
        var fabrica = Substitute.For<IUmbracoContextFactory>();
        fabrica.EnsureUmbracoContext().Returns(
            new UmbracoContextReference(Substitute.For<IUmbracoContext>(), false, Substitute.For<IUmbracoContextAccessor>()));
        var consulta = Substitute.For<IBlogQuery>();
        consulta.GetPosts(Arg.Any<BlogQueryRequest>()).Returns(articulos);
        var localizacion = Substitute.For<ILocalizationService>();
        localizacion.GetDefaultLanguageIsoCode().Returns("es-CO");
        var variacion = new Variacion();

        var sut = new NewsSitemapController(
            consulta,
            fabrica,
            variacion,
            localizacion,
            new RelojFijo(Ahora),
            Options.Create(new OutputCacheSettings { Disabled = true }),
            new MemoryCache(new MemoryCacheOptions()))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        sut.HttpContext.Request.Scheme = "http";
        sut.HttpContext.Request.Host = new HostString("blogs.test");
        return (sut, fabrica, consulta, variacion);
    }

    private static PostSummary Articulo(string url, DateTime publicado) => new(
        Url: url,
        Title: "Un artículo",
        Excerpt: null,
        HeroImageUrl: null,
        PublishDate: publicado,
        ReadTimeMinutes: null,
        CategoryName: null,
        Tags: Array.Empty<string>());

    private sealed class Variacion : IVariationContextAccessor
    {
        public VariationContext? VariationContext { get; set; }
    }

    private sealed class RelojFijo(DateTimeOffset ahora) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => ahora;
    }
}
