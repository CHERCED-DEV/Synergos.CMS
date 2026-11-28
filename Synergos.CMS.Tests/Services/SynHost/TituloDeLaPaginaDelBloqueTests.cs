using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// «Es el título de la página» (D-016-Y4): marcado, la pieza abre con h1; sin marcar o sin el ajuste no viaja nada y la
/// pieza usa su h2, para no darle un segundo h1 a una página que ya tiene el suyo.
/// </summary>
public sealed class TituloDeLaPaginaDelBloqueTests
{
    [Fact]
    public void Marcado_el_titulo_de_la_pieza_es_el_de_la_pagina() =>
        Assert.Equal(1, TituloDeLaPaginaDelBloque.Nivel(ElementoFalso.Con(("esTituloDeLaPagina", true))));

    [Fact]
    public void Sin_marcar_no_viaja_nivel() =>
        Assert.Null(TituloDeLaPaginaDelBloque.Nivel(ElementoFalso.Con(("esTituloDeLaPagina", false))));

    [Fact]
    public void Un_bloque_sin_el_ajuste_no_viaja_nivel() =>
        Assert.Null(TituloDeLaPaginaDelBloque.Nivel(ElementoFalso.Con(("heading", "Alquiler de equipos"))));
}

/// <summary>El h1 de la pieza que es el título de la página también antes de hidratar (D-016-Y4).</summary>
public sealed class TituloDeLaPaginaEnElRespaldoTests
{
    [Fact]
    public void Con_titulo_el_respaldo_lleva_un_h1_para_lectores_que_la_pieza_quita_al_montar()
    {
        var respaldo = Synergos.CMS.Web.Services.SynHostFallbackBuilder.TituloDeLaPagina("Catálogo & ofertas");

        Assert.Equal("<div class=\"syn-ssr-fallback syn-ssr-titulo\" data-syn-ssr-fallback=\"true\"><h1 class=\"syn-sr-only\">Catálogo &amp; ofertas</h1></div>", respaldo);
    }

    [Fact]
    public void Sin_titulo_no_hay_respaldo() =>
        Assert.Null(Synergos.CMS.Web.Services.SynHostFallbackBuilder.TituloDeLaPagina("  "));

    [Theory]
    [InlineData(true, "<h1 class=\"syn-ssr-ehr__title\">")]
    [InlineData(false, "<h2 class=\"syn-ssr-ehr__title\">")]
    public void El_panel_sin_sesion_titula_la_pagina_solo_si_el_bloque_lo_es(bool esTitulo, string esperado) =>
        Assert.Contains(esperado, Synergos.CMS.Web.Services.SynHostFallbackBuilder.EhrSinSesion("/healthcare/", esTitulo), StringComparison.Ordinal);
}
