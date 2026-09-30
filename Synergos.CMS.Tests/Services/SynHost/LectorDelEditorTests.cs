using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Models;
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

    // ── Enlace (Umbraco.MultiUrlPicker) ─────────────────────────────────────────────────────

    [Fact]
    public void Enlace_sin_poner_no_viaja()
    {
        Assert.Null(Lector(ElementoFalso.Con()).Enlace("ctaLink"));
        Assert.Equal(0, Anotados());
    }

    [Fact]
    public void Enlace_puesto_viaja_con_su_destino_su_texto_y_donde_abre()
    {
        var leido = Lector(ElementoFalso.Con(("policyLink", ElementoFalso.Enlace(" /privacidad ", " Política de privacidad ", "_blank"))))
            .Enlace("policyLink");

        Assert.Equal(new EnlaceDelEditor("/privacidad", "Política de privacidad", "_blank"), leido);
    }

    [Fact]
    public void Enlace_sin_texto_ni_ventana_nueva_viaja_solo_con_su_destino()
    {
        var leido = Lector(ElementoFalso.Con(("actionLink", ElementoFalso.Enlace("https://wa.me/573001234567", "", null))))
            .Enlace("actionLink");

        Assert.Equal(new EnlaceDelEditor("https://wa.me/573001234567", null, null), leido);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" # ")]
    public void Enlace_sin_destino_no_viaja_y_se_anota(string url)
    {
        Assert.Null(Lector(ElementoFalso.Con(("shareLink", ElementoFalso.Enlace(url, "Compartir")))).Enlace("shareLink"));
        Assert.Equal(1, Anotados());
    }

    [Fact]
    public void Enlace_que_llega_como_lista_toma_el_primero()
    {
        IEnumerable<Link> varios = [ElementoFalso.Enlace("/uno"), ElementoFalso.Enlace("/dos")];

        Assert.Equal("/uno", Lector(ElementoFalso.Con(("ctaLink", varios))).Enlace("ctaLink")?.Url);
    }

    [Fact]
    public void Enlace_leido_dos_veces_da_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con(("ctaLink", ElementoFalso.Enlace("/reservas", "Reservar"))));

        Assert.Equal(lector.Enlace("ctaLink"), lector.Enlace("ctaLink"));
    }

    // ── Opciones (Umbraco.DropDown.Flexible múltiple) ───────────────────────────────────────

    [Fact]
    public void Opciones_sin_marcar_no_viajan()
    {
        Assert.Null(Lector(ElementoFalso.Con()).Opciones("platforms"));
        Assert.Null(Lector(ElementoFalso.Con(("platforms", Array.Empty<string>()))).Opciones("platforms"));
    }

    [Fact]
    public void Opciones_marcadas_viajan_como_lista_en_su_orden()
    {
        var leidas = Lector(ElementoFalso.Con(("platforms", new[] { "whatsapp", "twitter", "email" }))).Opciones("platforms");

        Assert.Equal(new[] { "whatsapp", "twitter", "email" }, leidas);
    }

    [Fact]
    public void Opciones_vacias_o_repetidas_no_viajan_dos_veces()
    {
        var leidas = Lector(ElementoFalso.Con(("platforms", new[] { " whatsapp ", "", "  ", "whatsapp", "email" }))).Opciones("platforms");

        Assert.Equal(new[] { "whatsapp", "email" }, leidas);
    }

    [Fact]
    public void Un_desplegable_simple_se_lee_como_una_lista_de_una()
    {
        Assert.Equal(new[] { "bottom-left" }, Lector(ElementoFalso.Con(("position", "bottom-left"))).Opciones("position"));
        Assert.Null(Lector(ElementoFalso.Con(("position", ""))).Opciones("position"));
    }

    [Fact]
    public void Opciones_leidas_dos_veces_dan_lo_mismo()
    {
        var lector = Lector(ElementoFalso.Con(("platforms", new[] { "facebook", "linkedin" })));

        Assert.Equal(lector.Opciones("platforms"), lector.Opciones("platforms"));
    }
}
