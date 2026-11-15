using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La descripción al compartir (016, 4.ª vuelta, C9): 75 de 112 páginas del piloto salían sin ella.
/// </summary>
public sealed class DescripcionDeLaPaginaTests
{
    private static Func<string, string?> Pagina(params (string Alias, string? Valor)[] valores)
        => alias => valores.FirstOrDefault(v => v.Alias == alias).Valor;

    [Fact]
    public void La_de_la_pagina_gana_aunque_haya_resumen_marca_y_sitio()
    {
        var d = DescripcionDeLaPagina.Resolver(
            Pagina(("seoDescription", "La mía."), ("productDescriptionText", "El resumen.")), "La marca.", "El sitio.");

        Assert.Equal("La mía.", d);
    }

    [Fact]
    public void Una_ficha_sin_descripcion_usa_su_resumen_antes_que_la_marca_y_el_sitio()
    {
        var d = DescripcionDeLaPagina.Resolver(
            Pagina(("seoDescription", " "), ("productDescriptionText", "Ajustable, bordado de marca al frente.")), "La marca.", "El sitio.");

        Assert.Equal("Ajustable, bordado de marca al frente.", d);
    }

    [Fact]
    public void Una_pagina_sin_nada_propio_dice_lo_de_la_marca_y_si_no_lo_de_su_sitio()
    {
        Assert.Equal("La marca.", DescripcionDeLaPagina.Resolver(Pagina(), "La marca.", "El sitio."));
        Assert.Equal("El sitio.", DescripcionDeLaPagina.Resolver(Pagina(), null, "El sitio."));
        Assert.Null(DescripcionDeLaPagina.Resolver(Pagina(), null, ""));
    }

    [Fact]
    public void El_resumen_sale_plano_sin_etiquetas_ni_entidades()
    {
        var d = DescripcionDeLaPagina.Resolver(
            Pagina(("categoryDescription", "<p>Camisetas,   buzos &amp; gorras.</p>")), null, null);

        Assert.Equal("Camisetas, buzos & gorras.", d);
    }

    [Fact]
    public void Un_texto_largo_se_corta_en_una_palabra_entera_con_puntos_suspensivos()
    {
        var largo = string.Join(" ", Enumerable.Repeat("palabra", 40));

        var d = DescripcionDeLaPagina.Resolver(Pagina(("equipmentSummary", largo)), null, null)!;

        Assert.True(d.Length <= DescripcionDeLaPagina.Largo, d.Length.ToString());
        Assert.EndsWith("palabra…", d, StringComparison.Ordinal);
    }

    [Fact]
    public void Las_descripciones_del_hub_caben_no_se_repiten_y_cierran_la_frase()
    {
        var hub = DevContenidoDeVerticales.DescripcionesDelHub;

        Assert.NotEmpty(hub);
        Assert.Equal(hub.Length, hub.Select(h => h.Pagina).Distinct().Count());
        Assert.Equal(hub.Length, hub.Select(h => h.Descripcion).Distinct().Count());
        Assert.All(hub, h =>
        {
            Assert.InRange(h.Descripcion.Length, 40, DescripcionDeLaPagina.Largo);
            Assert.EndsWith(".", h.Descripcion, StringComparison.Ordinal);
        });
    }
}
