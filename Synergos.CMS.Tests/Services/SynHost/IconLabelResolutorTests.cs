using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="IconLabelResolutor"/>: <c>iconKey</c> llega como <c>iconName</c>, que es lo que el
/// elemento lee — con el alias, el icono se perdía al hidratar (D1).
/// </summary>
public sealed class IconLabelResolutorTests
{
    private static readonly IconLabelResolutor Resolutor = new(ElementoFalso.Fallback);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor.Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_icono_y_el_texto_viajan_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor.Resolver(ElementoFalso.Con(
            ("iconKey", "check"),
            ("labelText", "Envío gratis"))).Props);

        Assert.Equal(new[] { "iconName", "labelText" }, cable.Keys);
        Assert.Equal("check", cable["iconName"]?.ToString());
        Assert.Equal("Envío gratis", cable["labelText"]?.ToString());
    }

    [Fact]
    public void Un_texto_en_blanco_no_viaja_y_el_resto_se_recorta()
    {
        var props = Resolutor.Resolver(ElementoFalso.Con(("iconKey", " arrow-right "), ("labelText", "   "))).Props;

        Assert.Equal(new IconLabelProps("arrow-right", null), props);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("iconKey", "check"), ("labelText", "Listo"));

        Assert.Equal(Resolutor.Resolver(elemento), Resolutor.Resolver(elemento));
    }
}
