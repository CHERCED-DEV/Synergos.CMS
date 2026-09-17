using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="ShopLinePricing"/> — cuánto vale una unidad de una línea del carrito, y qué
/// pasa cuando no se puede saber (#123).
/// </summary>
/// <remarks>
/// <para><b>Por qué esta clase existe y por qué estos tests no existían.</b> La decisión vivía
/// dentro de <c>DefaultCartService.Hydrate</c>, que necesita un <c>IUmbracoContextAccessor</c> y
/// un árbol de <c>IPublishedContent</c>: simular eso cuesta más que el código que verifica, así
/// que <b>el sitio del repo donde se decide cuánto se cobra no tenía un solo test</b>. Es el mismo
/// movimiento que hicieron <c>EventContentRules</c> y <c>CourseContentRules</c> — sacar la regla a
/// lógica pura es lo que la hace verificable.</para>
///
/// <para><b>Los dos casos del fixture son los que el repo midió en vivo</b> —<c>"$89000"</c> y
/// <c>"49.000"</c>— y son los únicos que exigen la regla: con <c>"89000"</c> a secas el
/// <c>TryParse</c> viejo y éste dan lo mismo y el defecto pasa en verde.</para>
/// </remarks>
public sealed class ShopLinePricingTests
{
    private const string Variantes =
        """[{"sku":"TOTE-XL","priceDelta":15000},{"sku":"TOTE-S","priceDelta":-5000}]""";

    /// <summary>
    /// El precio que el carrito cobraba a <b>49</b> por un tote de 49 mil.
    /// </summary>
    /// <remarks>
    /// <c>decimal.TryParse("49.000", NumberStyles.Number, InvariantCulture)</c> devuelve
    /// <c>true</c> y <c>49</c>: el punto es separador DECIMAL ahí. No hay guarda de «&gt; 0» que
    /// lo vea, porque 49 es un precio perfectamente plausible. Hoy la línea no se cobra: se
    /// omite.
    /// </remarks>
    [Fact]
    public void Un_separador_de_miles_no_se_cobra_dividido_por_mil()
    {
        Assert.False(ShopLinePricing.TryUnitPrice("49.000", null, null, out var precio));
        Assert.Equal(0m, precio);
    }

    /// <summary>
    /// El precio que el carrito cobraba a <b>cero</b>.
    /// </summary>
    /// <remarks>
    /// Éste es el que hace falta separar del anterior: cero es un precio VÁLIDO, así que no falla
    /// en ninguna parte hasta que alguien compra. <c>false</c> —y no <c>0m</c>— es lo que impide
    /// que el llamador confunda «no se sabe» con «gratis».
    /// </remarks>
    [Fact]
    public void Un_precio_con_simbolo_no_se_cobra_en_cero()
    {
        Assert.False(ShopLinePricing.TryUnitPrice("$89000", null, null, out var precio));
        Assert.Equal(0m, precio);
    }

    [Fact]
    public void Un_precio_inequivoco_se_cobra_tal_cual()
    {
        Assert.True(ShopLinePricing.TryUnitPrice("89000", null, null, out var precio));
        Assert.Equal(89_000m, precio);
    }

    [Fact]
    public void El_delta_de_la_variante_se_suma_al_precio_base()
    {
        Assert.True(ShopLinePricing.TryUnitPrice("89000", Variantes, "TOTE-XL", out var precio));
        Assert.Equal(104_000m, precio);

        Assert.True(ShopLinePricing.TryUnitPrice("89000", Variantes, "TOTE-S", out var menos));
        Assert.Equal(84_000m, menos);
    }

    /// <summary>
    /// Sin precio base no hay línea, <b>aunque la variante tenga delta</b>.
    /// </summary>
    /// <remarks>
    /// El caso que el orden equivocado dejaría pasar: si el delta se aplicara antes de mirar el
    /// base, un <c>"$89000"</c> con la variante XL saldría a <b>15.000</b> — un precio entero
    /// fabricado a partir de un ajuste. Es la forma del <c>0m</c> con otro número.
    /// </remarks>
    [Fact]
    public void Sin_precio_base_la_variante_no_fabrica_un_precio()
    {
        Assert.False(ShopLinePricing.TryUnitPrice("$89000", Variantes, "TOTE-XL", out var precio));
        Assert.Equal(0m, precio);
    }

    /// <summary>
    /// El delta NO se lee con la regla del precio: es un número JSON, no texto de un TextBox.
    /// </summary>
    /// <remarks>
    /// Dicho como test para que no se «unifique» por parecido (#120): <c>priceDelta</c> llega
    /// como número dentro de <c>productVariantsJson</c>, así que no hay separador ambiguo que
    /// resolver. Un JSON roto vale 0 de delta desde siempre — es un ajuste sobre un precio que sí
    /// se leyó, no el precio.
    /// </remarks>
    [Theory]
    [InlineData("no es json")]
    [InlineData("{}")]
    [InlineData("""[{"sku":"OTRA","priceDelta":15000}]""")]
    [InlineData("""[{"sku":"TOTE-XL"}]""")]
    public void Un_delta_que_no_se_puede_leer_no_tumba_la_linea(string variantesJson)
    {
        Assert.True(ShopLinePricing.TryUnitPrice("89000", variantesJson, "TOTE-XL", out var precio));
        Assert.Equal(89_000m, precio);
    }
}
