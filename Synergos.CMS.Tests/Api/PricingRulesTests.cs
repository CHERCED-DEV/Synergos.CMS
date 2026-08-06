using Synergos.Api.Pricing.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="PricingRules"/> — descuentos, impuestos y monedas.</summary>
public sealed class PricingRulesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static Money Cop(decimal x) => Money.Of(x, "COP");

    private static Promotion Promo(DiscountKind kind, decimal value, string currency = "COP", int desdeH = -1, int hastaH = 1)
        => new("p1", "PROMO", kind, value, currency,
            TimeWindow.Of(Ahora.AddHours(desdeH), Ahora.AddHours(hastaH)));

    [Fact]
    public void Un_descuento_FIJO_nunca_deja_el_total_negativo()
    {
        // Un descuento de 50 000 sobre una compra de 30 000 se acota. La alternativa es un cobro
        // negativo, que aguas abajo se convierte en una devolución que nadie pidió.
        var r = PricingRules.Discount(Promo(DiscountKind.Fixed, 50_000m), Cop(30_000m));

        Assert.True(r.IsOk);
        Assert.Equal(Cop(30_000m), r.Value);
    }

    [Fact]
    public void Un_descuento_PORCENTUAL_se_calcula_en_puntos_basicos()
    {
        var r = PricingRules.Discount(Promo(DiscountKind.Percentage, 1500m), Cop(20_000m));

        Assert.True(r.IsOk);
        Assert.Equal(Cop(3_000m), r.Value);   // 15 %
    }

    [Fact]
    public void Un_descuento_fijo_en_OTRA_moneda_se_rechaza_en_vez_de_convertirse()
    {
        // No hay conversión correcta sin una tasa, y adivinarla en silencio es peor que fallar.
        var r = PricingRules.Discount(Promo(DiscountKind.Fixed, 5m, currency: "USD"), Cop(30_000m));

        Assert.False(r.IsOk);
        Assert.Equal("pricing.promotion_currency_mismatch", r.Rejection!.Code);
    }

    [Fact]
    public void Una_promocion_fuera_de_vigencia_da_EXPIRED_y_no_NotFound()
    {
        // Al cliente le sirve saber que el código era real pero se le pasó el plazo. Un NotFound
        // lo manda a revisar si lo escribió mal.
        var vencida = Promo(DiscountKind.Percentage, 1000m, desdeH: -10, hastaH: -5);

        Assert.Equal(RejectionKind.Expired, PricingRules.CheckPromotion(vencida, Ahora)?.Kind);
        Assert.Equal(RejectionKind.NotFound, PricingRules.CheckPromotion(null, Ahora)?.Kind);
        Assert.Null(PricingRules.CheckPromotion(Promo(DiscountKind.Percentage, 1000m), Ahora));
    }

    [Fact]
    public void El_impuesto_se_calcula_en_puntos_basicos()
    {
        Assert.Equal(Cop(1_900m), PricingRules.Tax(Cop(10_000m), 1900));   // 19 %
        Assert.Equal(Cop(0m), PricingRules.Tax(Cop(10_000m), 0));
    }

    [Fact]
    public void Un_precio_negativo_o_un_impuesto_fuera_de_rango_no_se_publican()
    {
        Assert.Equal("pricing.negative_price", PricingRules.CheckPrice(Cop(-1m), 0)?.Code);
        Assert.Equal("pricing.bad_tax_rate", PricingRules.CheckPrice(Cop(1m), -1)?.Code);
        Assert.Equal("pricing.bad_tax_rate", PricingRules.CheckPrice(Cop(1m), PricingRules.FullBasisPoints + 1)?.Code);
        Assert.Null(PricingRules.CheckPrice(Cop(0m), PricingRules.FullBasisPoints));
    }

    [Fact]
    public void Una_cantidad_de_cero_o_menos_no_cotiza()
    {
        Assert.Equal("pricing.bad_quantity", PricingRules.CheckQuantity(0)?.Code);
        Assert.Null(PricingRules.CheckQuantity(1));
    }
}
