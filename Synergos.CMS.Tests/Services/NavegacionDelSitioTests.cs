using NSubstitute;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Qué hijos del siteRoot salen en la navegación (#185): sólo las páginas.
/// </summary>
/// <remarks>
/// Medido en vivo sobre una copia de la base: al crear el primer aviso por la vía prevista
/// (<c>siteRoot › siteConfigFolder › transversalsRepository</c>), el header y el footer de
/// <c>/synergos/</c> mostraban «Configuración» enlazando a <c>/synergos/configuracion/</c>, que
/// contesta 404. La carpeta no tiene plantilla: Umbraco no tiene con qué pintarla.
/// </remarks>
public sealed class NavegacionDelSitioTests
{
    private static readonly IPublishedValueFallback Fallback = new NoopPublishedValueFallback();

    private static IPublishedContent Hijo(string nombre, int? plantilla, bool oculto = false, bool invisible = false)
    {
        var oculta = Substitute.For<IPublishedProperty>();
        oculta.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(oculto);
        oculta.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(oculto);

        var naviHide = Substitute.For<IPublishedProperty>();
        naviHide.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(invisible);
        naviHide.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(invisible);

        var nodo = Substitute.For<IPublishedContent>();
        nodo.Name.Returns(nombre);
        nodo.TemplateId.Returns(plantilla);
        nodo.GetProperty("hideFromMainMenu").Returns(oculta);
        nodo.GetProperty("umbracoNaviHide").Returns(naviHide);
        return nodo;
    }

    private static IPublishedContent SiteRoot(params IPublishedContent[] hijos)
    {
        var raiz = Substitute.For<IPublishedContent>();
        raiz.Children.Returns(hijos);
        return raiz;
    }

    private static IReadOnlyList<string?> Enlaces(IPublishedContent? siteRoot)
        => NavegacionDelSitio.Enlaces(siteRoot, "hideFromMainMenu", Fallback).Select(n => n.Name).ToList();

    [Fact]
    public void La_carpeta_de_configuracion_no_sale_en_la_navegacion()
    {
        var raiz = SiteRoot(
            Hijo("Identidad", plantilla: 1062),
            Hijo("Configuración", plantilla: null),
            Hijo("Contacto", plantilla: 1062));

        Assert.Equal(["Identidad", "Contacto"], Enlaces(raiz));
    }

    [Fact]
    public void Un_nodo_con_plantilla_cero_tampoco_es_una_pagina()
    {
        Assert.Empty(Enlaces(SiteRoot(Hijo("Evento", plantilla: 0))));
    }

    [Fact]
    public void Siguen_valiendo_los_dos_interruptores_del_editor()
    {
        var raiz = SiteRoot(
            Hijo("Visible", plantilla: 1062),
            Hijo("Fuera del menú", plantilla: 1062, oculto: true),
            Hijo("Oculta", plantilla: 1062, invisible: true));

        Assert.Equal(["Visible"], Enlaces(raiz));
    }

    [Fact]
    public void Sin_siteRoot_no_hay_enlaces()
    {
        Assert.Empty(Enlaces(null));
    }
}
