using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// «¿El catálogo ve ya este curso en la academia?», preguntado sin log (010-educacion-matricula): por el MISMO
/// recorrido que el catálogo, desde las raíces de la caché publicada hasta el <c>siteRoot</c> con el <c>brandKey</c> de
/// la academia, y el curso entre sus descendientes. El molde de <c>TiendaEnElContenidoTests</c>.
/// </summary>
public sealed class AcademiaEnElContenidoTests
{
    private static readonly Guid Curso = Guid.Parse("6a1c2d3e-4f50-6172-8394-a5b6c7d8e9f1");

    private readonly IPublishedContentCache _cache = Substitute.For<IPublishedContentCache>();

    private static IPublishedContent Nodo(int id, string alias, string? marca = null, Guid? llave = null, params IPublishedContent[] hijos)
    {
        var tipo = Substitute.For<IPublishedContentType>();
        tipo.Alias.Returns(alias);
        tipo.Variations.Returns(ContentVariation.Nothing);
        var nodo = Substitute.For<IPublishedContent>();
        nodo.Id.Returns(id);
        nodo.Key.Returns(llave ?? Guid.NewGuid());
        nodo.ContentType.Returns(tipo);
        nodo.ChildrenForAllCultures.Returns(hijos);
        if (marca is not null)
        {
            var propiedad = Substitute.For<IPublishedProperty>();
            propiedad.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(marca);
            nodo.GetProperty("brandKey").Returns(propiedad);
        }
        return nodo;
    }

    private static IPublishedContent ElCurso() => Nodo(1020, "coursePage", llave: Curso);

    private void EnLaRaiz(params IPublishedContent[] raices)
        => _cache.GetAtRoot(Arg.Any<string?>()).Returns(raices);

    private AcademiaEnElContenido Montar(string? alcance = "scholar", bool conContexto = true)
    {
        var contexto = Substitute.For<IUmbracoContext>();
        contexto.Content.Returns(_cache);
        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(x =>
        {
            x[0] = conContexto ? contexto : null!;
            return conContexto;
        });
        var ajustes = Substitute.For<IOptionsMonitor<CatalogSettings>>();
        var scopes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (alcance is not null) scopes["Academy"] = alcance;
        ajustes.CurrentValue.Returns(new CatalogSettings { Scopes = scopes });
        return new AcademiaEnElContenido(accesor, ajustes, Substitute.For<IVariationContextAccessor>());
    }

    [Fact]
    public void Un_curso_publicado_bajo_el_siteRoot_de_la_academia_se_ve_aunque_su_marca_traiga_espacios_o_mayusculas()
    {
        // Cada nodo se arma ANTES de su Returns: armar un sustituto dentro de otro Returns lo rompe.
        var curso = ElCurso();
        var seccion = Nodo(1010, "academyPage", hijos: curso);
        var academia = Nodo(1000, "siteRoot", " Scholar ", hijos: seccion);
        var inicio = Nodo(900, "homePage", hijos: academia);
        EnLaRaiz(inicio);

        Assert.True(Montar().Ve(Curso));
    }

    [Fact]
    public void Si_el_recorrido_todavia_no_alcanza_la_academia_no_se_ve_aunque_la_cache_lo_tenga_por_id()
    {
        var curso = ElCurso();
        var academia = Nodo(1000, "siteRoot", "scholar", hijos: curso);
        _cache.GetById(Curso).Returns(curso);
        _cache.GetById(1000).Returns(academia);
        EnLaRaiz();

        Assert.False(Montar().Ve(Curso));
    }

    [Fact]
    public void Un_curso_de_otro_sitio_no_es_de_la_academia_aunque_la_academia_se_vea()
    {
        var curso = ElCurso();
        var otroSitio = Nodo(2000, "siteRoot", "ecommerce", hijos: curso);
        var academia = Nodo(1000, "siteRoot", "scholar", hijos: Nodo(1030, "coursePage"));
        EnLaRaiz(otroSitio, academia);

        Assert.False(Montar().Ve(Curso));
    }

    [Fact]
    public void Sin_el_alcance_de_la_academia_o_sin_contexto_no_se_ve_nada()
    {
        var curso = ElCurso();
        var academia = Nodo(1000, "siteRoot", "scholar", hijos: curso);
        EnLaRaiz(academia);

        Assert.False(Montar(alcance: null).Ve(Curso));
        Assert.False(Montar(conContexto: false).Ve(Curso));
    }
}
