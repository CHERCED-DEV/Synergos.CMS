using Synergos.Api.Payments.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Api;

/// <summary>Cubre <see cref="PaymentRules"/> — lo que el cobro rechaza solo.</summary>
public sealed class PaymentRulesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 3, 2, 10, 0, 0, TimeSpan.Zero);
    private static Money Cop(decimal x) => Money.Of(x, "COP");

    private static Payment Pago(PaymentStatus estado, decimal monto = 100_000m, params decimal[] devueltos)
        => new("pg1", Ref.Create("orders.pedido", "o-1"), Ref.Create("identity.member", "m-1"),
            Cop(monto), estado, "stub", "ref-1",
            devueltos.Select((d, i) => new Refund($"r{i}", Cop(d), null, Ahora)).ToList(), Ahora,
            estado == PaymentStatus.Captured ? Ahora : null);

    [Fact]
    public void Un_cobro_de_CERO_se_rechaza()
    {
        // Si es gratis, no hay pago que registrar — y un pago de cero ensucia la conciliación
        // con filas que no corresponden a ningún movimiento.
        Assert.Equal("payments.zero_amount", PaymentRules.CheckAmount(Cop(0m))?.Code);
        Assert.Equal("payments.negative_amount", PaymentRules.CheckAmount(Cop(-1m))?.Code);
        Assert.Null(PaymentRules.CheckAmount(Cop(1m)));
    }

    [Fact]
    public void Solo_se_captura_lo_AUTORIZADO()
    {
        Assert.Null(PaymentRules.CheckCapturable(Pago(PaymentStatus.Authorized)));
        Assert.Equal("payments.already_captured", PaymentRules.CheckCapturable(Pago(PaymentStatus.Captured))?.Code);
        Assert.Equal("payments.voided", PaymentRules.CheckCapturable(Pago(PaymentStatus.Voided))?.Code);
        Assert.Equal("payments.payment_failed", PaymentRules.CheckCapturable(Pago(PaymentStatus.Failed))?.Code);
    }

    [Fact]
    public void Liberar_lo_ya_liberado_NO_es_un_error()
    {
        // Rechazarlo obligaría a cada cliente a distinguir "no pude" de "ya estaba", que es el
        // ruido que una compensación debería ahorrarle justo cuando algo ya salió mal.
        Assert.Null(PaymentRules.CheckVoidable(Pago(PaymentStatus.Voided)));
    }

    [Fact]
    public void Un_cobro_CAPTURADO_no_se_libera_se_devuelve()
    {
        Assert.Equal("payments.already_captured", PaymentRules.CheckVoidable(Pago(PaymentStatus.Captured))?.Code);
    }

    [Fact]
    public void Solo_se_devuelve_lo_CAPTURADO()
    {
        Assert.Equal("payments.not_captured", PaymentRules.CheckRefundable(Pago(PaymentStatus.Authorized), Cop(1m))?.Code);
    }

    [Fact]
    public void Las_devoluciones_PARCIALES_se_acumulan_y_no_pasan_de_lo_capturado()
    {
        // Es el modo de fallo más caro de esta capacidad: dos devoluciones parciales que sumadas
        // devuelven más de lo que entró.
        var conDosDevoluciones = Pago(PaymentStatus.Captured, 100_000m, 60_000m, 30_000m);

        Assert.Equal(Cop(90_000m), conDosDevoluciones.Refunded);
        Assert.Equal(Cop(10_000m), conDosDevoluciones.Refundable);
        Assert.Null(PaymentRules.CheckRefundable(conDosDevoluciones, Cop(10_000m)));
        Assert.Equal("payments.refund_over_captured",
            PaymentRules.CheckRefundable(conDosDevoluciones, Cop(10_001m))?.Code);
    }

    [Fact]
    public void Una_devolucion_en_otra_moneda_se_rechaza()
    {
        Assert.Equal("payments.refund_currency_mismatch",
            PaymentRules.CheckRefundable(Pago(PaymentStatus.Captured), Money.Of(5m, "USD"))?.Code);
    }

    [Fact]
    public void Lo_NO_capturado_no_tiene_nada_devoluble()
    {
        // Refundable tiene que dar cero y no el monto: si diera el monto, un cliente podría
        // pedir una devolución de plata que nunca se movió.
        Assert.True(Pago(PaymentStatus.Authorized).Refundable.IsZero);
        Assert.True(Pago(PaymentStatus.Voided).Refundable.IsZero);
    }
}
