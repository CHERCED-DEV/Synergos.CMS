using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Dictionary;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="HeroBannerResolutor"/>: la foto y el botón llegan como <c>media</c> y
/// <c>ctaLink</c> —la vista los mandaba como <c>mediaUrl</c>/<c>ctaUrl</c> y al hidratar el hero
/// los perdía (D1)— y el respaldo SSR sale de los mismos valores.
/// </summary>
public sealed class HeroBannerResolutorTests
{
    private readonly ILogger<HeroBannerResolutor> _log = Substitute.For<ILogger<HeroBannerResolutor>>();

    private HeroBannerResolutor Resolutor(ICultureDictionaryFactory? diccionario = null)
        => new(ElementoFalso.Fallback, ElementoFalso.Urls(), diccionario ?? ElementoFalso.Diccionario(), _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave_ni_respaldo()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con());

        Assert.Empty(SolicitudSynHost.Props(resuelto.Props));
        Assert.Null(resuelto.RespaldoHtml);
    }

    [Fact]
    public void La_foto_y_el_boton_viajan_con_los_nombres_que_lee_el_elemento_y_el_respaldo_los_pinta()
    {
        var resuelto = Resolutor().Resolver(ElementoFalso.Con(
            ("title", "Viví el Caribe"),
            ("subtitle", "Temporada 2026"),
            ("media", ElementoFalso.Medio("/media/hero/playa.jpg", "Playa al atardecer")),
            ("ctaLabel", "Reservar"),
            ("ctaLink", ElementoFalso.Enlace("/reservas", "Ver reservas", "_blank"))));

        var cable = SolicitudSynHost.Props(resuelto.Props);
        Assert.Equal(new[] { "title", "subtitle", "media", "mediaAlt", "ctaLabel", "ctaLink" }, cable.Keys);
        Assert.Equal("/media/hero/playa.jpg", cable["media"]!.ToString());
        Assert.Equal("/reservas", cable["ctaLink"]!.ToString());
        Assert.Contains("src=\"/media/hero/playa.jpg\"", resuelto.RespaldoHtml, StringComparison.Ordinal);
        Assert.Contains("alt=\"Playa al atardecer\"", resuelto.RespaldoHtml, StringComparison.Ordinal);
        Assert.Contains("href=\"/reservas\">Reservar</a>", resuelto.RespaldoHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void Sin_texto_del_boton_viaja_el_texto_del_enlace()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("title", "T"),
            ("ctaLink", ElementoFalso.Enlace("/reservas", "Ver reservas")))).Props;

        Assert.Equal("Ver reservas", props.CtaLabel);
        Assert.Equal("/reservas", props.CtaLink);
    }

    [Fact]
    public void Un_medio_o_un_enlace_sin_destino_no_viajan_y_se_anotan()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("title", "T"),
            ("media", ElementoFalso.Medio("")),
            ("ctaLabel", "Reservar"),
            ("ctaLink", ElementoFalso.Enlace("#")))).Props;

        Assert.Equal(new HeroBannerProps("T", null, null, null, "Reservar", null), props);
        Assert.Equal(2, _log.ReceivedCalls().Count(c => c.GetMethodInfo().Name == nameof(ILogger.Log)));
    }

    [Fact]
    public void Sin_titulo_el_respaldo_nombra_la_seccion_con_el_diccionario()
    {
        var respaldo = Resolutor(ElementoFalso.Diccionario(("Synhost.Hero.Aria", "Destacado")))
            .Resolver(ElementoFalso.Con(("subtitle", "Temporada 2026"))).RespaldoHtml;

        Assert.Contains("aria-label=\"Destacado\"", respaldo, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("title", "T"), ("media", ElementoFalso.Medio("/m.jpg")));

        Assert.Equal(Resolutor().Resolver(elemento), Resolutor().Resolver(elemento));
    }
}
