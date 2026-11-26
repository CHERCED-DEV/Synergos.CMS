using Synergos.CMS.Tests.Services.SynHost;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El espaciado a los lados de las partes fijas de una plantilla de página (D-012-V13, 012-blogs-social): la cabecera de
/// un artículo quedaba pegada al borde en el teléfono. Sale de su composición (<c>compDomSpacing</c>), como el de una
/// sección.
/// </summary>
public sealed class EspaciadoLateralTests
{
    [Fact]
    public void Sin_valor_del_editor_lleva_el_del_preset_de_seccion_y_con_valor_el_que_eligio()
    {
        Assert.Equal("syn-space--inline-lg", LayoutCssBuilder.EspaciadoLateral(ElementoFalso.Con(), ElementoFalso.Fallback));
        Assert.Equal(
            "syn-space--inline-lg",
            LayoutCssBuilder.EspaciadoLateral(ElementoFalso.Con(("spacingInline", "  ")), ElementoFalso.Fallback));
        Assert.Equal(
            "syn-space--inline-md",
            LayoutCssBuilder.EspaciadoLateral(ElementoFalso.Con(("spacingInline", "md")), ElementoFalso.Fallback));
    }
}
