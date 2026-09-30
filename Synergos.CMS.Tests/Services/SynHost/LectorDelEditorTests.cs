using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Routing;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre las lecturas de <see cref="LectorDelEditor"/> que traducen un DataType de Umbraco a lo
/// que viaja (ADR 0135): cada una se escribe UNA vez y la usan todos los resolvers. Las lecturas
/// del piloto (texto, números, JSON) las cubren los tests de sus resolvers.
/// </summary>
public sealed class LectorDelEditorTests
{
    private readonly ILogger _log = Substitute.For<ILogger>();

    private int Anotados() => _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log));

    private LectorDelEditor Lector(IPublishedElement elemento, IPublishedUrlProvider? urls = null)
        => new(elemento, ElementoFalso.Fallback, _log, urls ?? ElementoFalso.Urls());

    // ── Medio (Umbraco.MediaPicker3) ────────────────────────────────────────────────────────

    [Fact]
    public void Medio_sin_elegir_no_viaja()
    {
        Assert.Null(Lector(ElementoFalso.Con()).Medio("audioFile"));
        Assert.Equal(0, Anotados());
    }

    [Fact]
    public void Medio_elegido_viaja_como_su_url_absoluta_y_su_texto_alternativo()
    {
        var urls = ElementoFalso.Urls();
        var medio = ElementoFalso.Medio("/media/ana.jpg", "  Ana Gómez, directora  ");

        var leido = Lector(ElementoFalso.Con(("avatarImage", medio)), urls).Medio("avatarImage");

        Assert.Equal(new MedioDelEditor("/media/ana.jpg", "Ana Gómez, directora"), leido);
        urls.Received(1).GetMediaUrl(medio, UrlMode.Absolute, null, "umbracoFile", null);
    }

    [Fact]
    public void Medio_sin_texto_alternativo_viaja_sin_alt()
    {
        var leido = Lector(ElementoFalso.Con(("audioFile", ElementoFalso.Medio("/media/a.mp3")))).Medio("audioFile");

        Assert.Equal(new MedioDelEditor("/media/a.mp3", null), leido);
    }

    [Theory]
    [InlineData("")]
    [InlineData("#")]
    public void Medio_sin_fichero_no_viaja_y_se_anota(string url)
    {
        var leido = Lector(ElementoFalso.Con(("videoFile", ElementoFalso.Medio(url)))).Medio("videoFile");

        Assert.Null(leido);
        Assert.Equal(1, Anotados());
    }

    [Fact]
    public void Medio_que_llega_como_lista_toma_el_primero()
    {
        IEnumerable<IPublishedContent> varios = [ElementoFalso.Medio("/media/1.jpg"), ElementoFalso.Medio("/media/2.jpg")];

        var leido = Lector(ElementoFalso.Con(("media", varios))).Medio("media");

        Assert.Equal("/media/1.jpg", leido?.Url);
    }

    [Fact]
    public void Medio_leido_dos_veces_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("posterImage", ElementoFalso.Medio("/media/p.jpg", "Póster")));
        var lector = Lector(elemento);

        Assert.Equal(lector.Medio("posterImage"), lector.Medio("posterImage"));
    }

    [Fact]
    public void Medio_sin_proveedor_de_urls_es_un_defecto_del_resolver_y_lanza()
    {
        var lector = new LectorDelEditor(ElementoFalso.Con(("audioFile", ElementoFalso.Medio("/a.mp3"))), ElementoFalso.Fallback);

        Assert.Throws<InvalidOperationException>(() => lector.Medio("audioFile"));
    }
}
