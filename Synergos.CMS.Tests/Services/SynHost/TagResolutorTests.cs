using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="TagResolutor"/>: <c>tagLabel</c>/<c>tagColor</c> llegan como <c>label</c>/<c>color</c>,
/// que es lo que el elemento lee — con los alias el chip desaparecía al hidratar (D1).
/// </summary>
public sealed class TagResolutorTests
{
    private static readonly TagResolutor Resolutor = new(ElementoFalso.Fallback);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        var resuelto = Resolutor.Resolver(ElementoFalso.Con());

        Assert.Empty(SolicitudSynHost.Props(resuelto.Props));
        Assert.Null(resuelto.RespaldoHtml);
    }

    [Fact]
    public void La_etiqueta_y_el_tono_viajan_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(
            Resolutor.Resolver(ElementoFalso.Con(("tagLabel", "Oferta"), ("tagColor", "success"))).Props);

        Assert.Equal(new[] { "label", "color" }, cable.Keys);
        Assert.Equal("Oferta", cable["label"]?.ToString());
        Assert.Equal("success", cable["color"]?.ToString());
    }

    [Fact]
    public void El_tono_se_normaliza_a_minusculas_y_un_texto_en_blanco_no_viaja()
    {
        var props = Resolutor.Resolver(ElementoFalso.Con(("tagLabel", "  "), ("tagColor", " Brand "))).Props;

        Assert.Equal(new TagProps(null, "brand"), props);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("tagLabel", "Oferta"));

        Assert.Equal(Resolutor.Resolver(elemento), Resolutor.Resolver(elemento));
    }
}
