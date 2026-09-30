using Microsoft.Extensions.Logging;
using NSubstitute;
using Synergos.CMS.Interfaces.SynHost;
using Synergos.CMS.Web.Services.SynHost;
using Umbraco.Cms.Core.Strings;

namespace Synergos.CMS.Tests.Services.SynHost;

/// <summary>
/// Cubre <see cref="RichTooltipResolutor"/>: el RTE llega como <c>body</c> en texto plano —la vista
/// mandaba el HTML como <c>tooltipContent</c> y el tooltip no abría (D1)— y el placement como el
/// lado que el elemento pinta.
/// </summary>
public sealed class RichTooltipResolutorTests
{
    private readonly ILogger<RichTooltipResolutor> _log = Substitute.For<ILogger<RichTooltipResolutor>>();

    private RichTooltipResolutor Resolutor() => new(ElementoFalso.Fallback, _log);

    [Fact]
    public void Un_bloque_sin_nada_autorado_no_manda_ninguna_clave()
    {
        Assert.Empty(SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con()).Props));
    }

    [Fact]
    public void El_contenido_viaja_como_body_en_texto_plano_con_los_nombres_que_lee_el_elemento()
    {
        var cable = SolicitudSynHost.Props(Resolutor().Resolver(ElementoFalso.Con(
            ("triggerText", "IVA"),
            ("tooltipContent", new HtmlEncodedString("<p>Impuesto al <strong>valor</strong> agregado.</p>")),
            ("placement", "bottom"))).Props);

        Assert.Equal(new[] { "triggerText", "body", "placement" }, cable.Keys);
        Assert.Equal("Impuesto al valor agregado.", cable["body"]!.ToString());
        Assert.False(cable.ContainsKey("tooltipContent"));
    }

    [Theory]
    [InlineData("bottom-start", "bottom")]
    [InlineData("top-end", "top")]
    [InlineData("Left", "left")]
    public void El_placement_viaja_como_el_lado_que_pinta_el_elemento(string delDataType, string lado)
    {
        Assert.Equal(lado, Resolutor().Resolver(ElementoFalso.Con(("placement", delDataType))).Props.Placement);
    }

    [Fact]
    public void Un_RTE_sin_texto_no_manda_body()
    {
        var props = Resolutor().Resolver(ElementoFalso.Con(
            ("triggerText", "IVA"),
            ("tooltipContent", new HtmlEncodedString("<p>&nbsp;</p>")))).Props;

        Assert.Equal(new RichTooltipProps("IVA", null, null), props);
    }

    [Fact]
    public void Resolver_dos_veces_el_mismo_bloque_da_lo_mismo()
    {
        var elemento = ElementoFalso.Con(("tooltipContent", "<p>Uno</p><p>Dos</p>"), ("placement", "right"));

        Assert.Equal(Resolutor().Resolver(elemento).Props, Resolutor().Resolver(elemento).Props);
    }
}
