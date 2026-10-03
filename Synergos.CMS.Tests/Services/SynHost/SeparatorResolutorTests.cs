using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="SeparatorResolutor"/> (#192, caso 2): el estilo que elige el editor viaja como
/// <c>style</c>, declarado en el record —antes salía de un diccionario libre en la vista, fuera del
/// contrato y del gate de vocabulario—.
/// </summary>
public sealed class SeparatorResolutorTests
{
    private static readonly SeparatorResolutor Resolutor = new(ElementoFalso.Fallback);

    [Fact]
    public void Un_bloque_sin_estilo_no_manda_ninguna_clave()
    {
        var resuelto = Resolutor.Resolver(ElementoFalso.Con());

        Assert.Empty(SolicitudSynHost.Props(resuelto.Props));
        Assert.Null(resuelto.RespaldoHtml);
    }

    [Fact]
    public void El_estilo_viaja_como_style()
    {
        var cable = SolicitudSynHost.Props(Resolutor.Resolver(ElementoFalso.Con(("style", "dashed"))).Props);

        Assert.Equal(new[] { "style" }, cable.Keys);
        Assert.Equal("dashed", cable["style"]?.ToString());
    }

    [Fact]
    public void El_estilo_se_normaliza_a_minusculas_y_un_blanco_no_viaja()
    {
        Assert.Equal(new SeparatorProps("gradient"), Resolutor.Resolver(ElementoFalso.Con(("style", " Gradient "))).Props);
        Assert.Equal(new SeparatorProps(null), Resolutor.Resolver(ElementoFalso.Con(("style", "  "))).Props);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("style", "dotted"));

        Assert.Equal(Resolutor.Resolver(elemento), Resolutor.Resolver(elemento));
    }
}
