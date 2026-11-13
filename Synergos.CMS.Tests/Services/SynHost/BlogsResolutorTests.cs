using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// <c>elementSynBlogs</c> → <see cref="BlogsProps"/> (012-blogs-social, I3): qué parte de la red es la página la elige
/// el editor en el bloque, y dónde viven las demás lo dice el sitio, como en la tienda.
/// </summary>
public sealed class BlogsResolutorTests
{
    [Fact]
    public void Copia_lo_del_editor_y_la_api_del_sitio()
    {
        var props = Resuelto(SinRutas(), ("heading", "Conecta y publica"), ("subheading", "La red del sitio"));

        Assert.Equal("Conecta y publica", props.Heading);
        Assert.Equal("La red del sitio", props.Subheading);
        Assert.Equal("/api/blogs", props.ApiBase);
    }

    [Fact]
    public void La_vista_es_la_que_elige_el_editor_y_las_rutas_las_del_sitio_con_el_inicio_por_defecto()
    {
        var rutas = Substitute.For<IRutasDeLasVistas>();
        rutas.Del(BlogsResolutor.AliasDelBloque, "inicio").Returns(
            [new RutaDeUnaVista("inicio", "/blogs/"), new RutaDeUnaVista("explorar", "/blogs/explorar/")]);

        var props = Resuelto(rutas, ("heading", "Blogs"), ("vista", "explorar"));

        Assert.Equal("explorar", props.Vista);
        Assert.Equal([new RutaDeUnaVista("inicio", "/blogs/"), new RutaDeUnaVista("explorar", "/blogs/explorar/")], props.Rutas);
    }

    [Fact]
    public void Sin_vista_elegida_no_viaja_y_el_elemento_abre_en_el_inicio()
    {
        var props = Resuelto(SinRutas(), ("heading", "Blogs"));

        Assert.Null(props.Vista);
        Assert.Empty(props.Rutas);
    }

    private static BlogsProps Resuelto(IRutasDeLasVistas rutas, params (string Alias, object? Valor)[] propiedades)
    {
        var negocio = Substitute.For<INegocioDelSitio<NegocioDeBlogs>>();
        negocio.Actual().Returns(new NegocioDeBlogs("/api/blogs"));
        return new BlogsResolutor(ElementoFalso.Fallback, negocio, rutas, NullLogger<BlogsResolutor>.Instance)
            .Resolver(ElementoFalso.Con(propiedades))
            .Props;
    }

    private static IRutasDeLasVistas SinRutas()
    {
        var rutas = Substitute.For<IRutasDeLasVistas>();
        rutas.Del(Arg.Any<string>(), Arg.Any<string>()).Returns([]);
        return rutas;
    }
}
