using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="DestinosApagados"/> (002-produccion, P11 C): un enlace del editor está «apagado» sólo si lleva a
/// una página que existe entre los borradores y no entre lo publicado. Lo demás —externo, una ruta que no es página,
/// una página que no existe, sin contexto de Umbraco— se pinta como siempre.
/// </summary>
public sealed class DestinosApagadosTests
{
    private readonly IPublishedContentCache _paginas = Substitute.For<IPublishedContentCache>();
    private readonly ILogger<DestinosApagados> _log = Substitute.For<ILogger<DestinosApagados>>();

    private DestinosApagados Destinos(bool conContexto = true)
    {
        var contexto = Substitute.For<IUmbracoContext>();
        contexto.Content.Returns(_paginas);
        var accesor = Substitute.For<IUmbracoContextAccessor>();
        accesor.TryGetUmbracoContext(out Arg.Any<IUmbracoContext>()!).Returns(x =>
        {
            x[0] = conContexto ? contexto : null!;
            return conContexto;
        });
        return new DestinosApagados(accesor, _log);
    }

    private void Pagina(string ruta, bool publicada)
    {
        var pagina = Substitute.For<IPublishedContent>();
        _paginas.GetByRoute(true, ruta, Arg.Any<bool?>(), Arg.Any<string?>()).Returns(pagina);
        _paginas.GetByRoute(false, ruta, Arg.Any<bool?>(), Arg.Any<string?>()).Returns(publicada ? pagina : null);
    }

    /// <summary>
    /// Ni publicada ni en borrador. Hay que decirlo: sin configurar, NSubstitute devuelve un contenido falso, no
    /// <c>null</c>, y la ruta parecería una página publicada.
    /// </summary>
    private void SinPagina(string ruta)
        => _paginas.GetByRoute(Arg.Any<bool>(), ruta, Arg.Any<bool?>(), Arg.Any<string?>()).Returns((IPublishedContent?)null);

    private int Anotadas() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    [Fact]
    public void Una_pagina_despublicada_esta_apagada_y_se_anota()
    {
        Pagina("/healthcare", publicada: false);

        Assert.True(Destinos().Apagado("/healthcare", ElementoFalso.Con(), "apps"));
        Assert.Equal(1, Anotadas());
    }

    [Fact]
    public void Una_pagina_publicada_no_esta_apagada()
    {
        Pagina("/synergos/apps/healthcare", publicada: true);

        Assert.False(Destinos().Apagado("/Synergos/Apps/Healthcare/", ElementoFalso.Con(), "ctaLink"));
        Assert.Equal(0, Anotadas());
    }

    [Fact]
    public void La_ruta_se_busca_como_la_busca_Umbraco()
    {
        Pagina("/synergos/apps/healthcare", publicada: false);

        Assert.True(Destinos().Apagado("/Synergos/Apps/Healthcare/?desde=portada#inicio", ElementoFalso.Con(), "ctaLink"));
    }

    [Fact]
    public void Una_ruta_que_no_es_una_pagina_no_esta_apagada()
    {
        // Ni publicada ni en borrador: una app, la API o una página que no existe. No se sabe apagada.
        SinPagina("/v1/rutas-de-una-app");

        Assert.False(Destinos().Apagado("/v1/rutas-de-una-app", ElementoFalso.Con(), "apps"));
        Assert.Equal(0, Anotadas());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://otro.example/healthcare")]
    [InlineData("//otro.example/healthcare")]
    [InlineData("mailto:hola@example.com")]
    [InlineData("#healthcare")]
    [InlineData("healthcare")]
    public void Lo_que_no_es_una_ruta_local_no_se_busca(string? url)
    {
        Assert.False(Destinos().Apagado(url, ElementoFalso.Con(), "apps"));
        Assert.Empty(_paginas.ReceivedCalls());
    }

    [Fact]
    public void Sin_contexto_de_Umbraco_no_esta_apagada()
    {
        Pagina("/healthcare", publicada: false);

        Assert.False(Destinos(conContexto: false).Apagado("/healthcare", ElementoFalso.Con(), "apps"));
    }

    [Fact]
    public void La_tarjeta_no_se_pinta_si_su_enlace_lleva_a_una_pagina_apagada()
    {
        // Una vista no tiene test de comportamiento —el runtime la compila en caliente—: la guardia es de texto, y la
        // de comportamiento es la pasada en vivo (despublicar Healthcare y volver a publicarla).
        var vista = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Views", "Partials", "Elements", "Comp", "Card.cshtml"));

        Assert.Contains("@inject Synergos.CMS.Web.Services.SynHost.IDestinosApagados Destinos", vista, StringComparison.Ordinal);
        var pregunta = vista.IndexOf("if (Destinos.Apagado(ctaUrl, Model, \"ctaLink\")) { return; }", StringComparison.Ordinal);
        Assert.InRange(pregunta, 0, vista.IndexOf("<article", StringComparison.Ordinal));
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

    [Theory]
    [InlineData("/healthcare", "/healthcare")]
    [InlineData("/Synergos/Apps/Healthcare/", "/synergos/apps/healthcare")]
    [InlineData("/tienda?x=1#y", "/tienda")]
    [InlineData("/educaci%C3%B3n", "/educación")]
    [InlineData(" / ", "/")]
    public void La_ruta_local(string url, string ruta)
    {
        Assert.Equal(ruta, DestinosApagados.RutaLocal(url));
    }
}
