using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// La ficha de un producto promete sólo lo que la tienda declara (016-contenido-piezas): antes pintaba siempre
/// «Envío gratis · Devoluciones en 30 días · Garantía del fabricante» en un sitio que avisa que no hace envíos.
/// </summary>
public sealed class PromesasDeLaFichaTests
{
    private static readonly Dictionary<string, string?> Diccionario = new()
    {
        ["Shop.Product.Trust.Shipping"] = "Envío gratis a todo el país",
        ["Shop.Product.Trust.Returns"] = "Devoluciones en 30 días",
        ["Shop.Product.Trust.Warranty"] = "Garantía del fabricante",
    };

    private static string? Texto(string clave) => Diccionario.TryGetValue(clave, out var t) ? t : null;

    [Fact]
    public void Sin_declarar_no_promete_nada()
    {
        Assert.Empty(PromesasDeLaFicha.Textos(null, Texto));
        Assert.Empty(PromesasDeLaFicha.Textos([], Texto));
    }

    [Fact]
    public void Promete_sólo_lo_declarado_en_el_orden_de_siempre_y_sin_nombres_que_no_conoce()
    {
        var textos = PromesasDeLaFicha.Textos(["garantia", " Envio ", "Regalo"], Texto);

        Assert.Equal(["Envío gratis a todo el país", "Garantía del fabricante"], textos);
    }

    [Fact]
    public void Una_promesa_declarada_sin_texto_en_el_diccionario_no_se_pinta()
    {
        Assert.Empty(PromesasDeLaFicha.Textos(["Devoluciones"], _ => "  "));
    }
}
