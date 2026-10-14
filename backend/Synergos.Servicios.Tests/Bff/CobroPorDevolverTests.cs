using Synergos.Bff.Core;
using Synergos.Bff.Eventos.Contracts;
using Synergos.Bff.Eventos.Domain;
using Synergos.Core;

namespace Synergos.CMS.Tests.Bff;

/// <summary>
/// La compra de entradas dice si hay plata cobrada sin devolver (ADR 0140, el cupón endurecido): el
/// estado no lo dice, porque deshacerse antes y después de capturar se llama igual.
/// </summary>
/// <remarks>
/// «Cerrar» captura y después consume el aforo: con el apartado vencido, la compra se deshace con el
/// cobro hecho, y lo que se deshace es una DEVOLUCIÓN. Quien lee sólo <c>Compensating</c> le decía a la
/// persona «no se te cobró nada».
/// </remarks>
public sealed class CobroPorDevolverTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 10, 14, 12, 0, 0, TimeSpan.Zero);

    private static TicketingSaga Saga(SagaStatus estado, params Compensation[] compensaciones) => new(
        "pta-1", Ref.Create("eventos.comprador", "ana"), "evt-1", estado,
        [new SeatHold("h1", "i1", 2, "GEN", null)], "pay-1", Money.Of(100_000m, "COP"), compensaciones, null, Ahora);

    private static Compensation Pendiente(string kind) => Compensation.For(kind, "pay-1", "no se completó");

    private static Compensation Hecha(string kind) => Pendiente(kind) with { DoneAtUtc = Ahora };

    private static Compensation Rendida(string kind) => Pendiente(kind) with { Attempts = CompensationLimits.MaxAttempts };

    [Fact]
    public void Deshaciendose_despues_de_capturar_hay_cobro_por_devolver()
    {
        var saga = Saga(SagaStatus.Compensating, Hecha(EventosCompensations.RestockSeats), Pendiente(EventosCompensations.RefundPayment));

        Assert.True(TicketPurchaseResponse.From(saga).RefundPending);
    }

    [Fact]
    public void Rendida_con_la_devolucion_colgada_sigue_habiendo_cobro_por_devolver()
    {
        var saga = Saga(SagaStatus.CompensationFailed, Hecha(EventosCompensations.RestockSeats), Rendida(EventosCompensations.RefundPayment));

        Assert.True(TicketPurchaseResponse.From(saga).RefundPending);
    }

    [Theory]
    [InlineData(SagaStatus.Compensating)]
    [InlineData(SagaStatus.CompensationFailed)]
    public void Deshaciendose_antes_de_capturar_no_hay_cobro_que_devolver(SagaStatus estado)
    {
        // Lo que se deshace es una autorización (se anula) y lo apartado: no se cobró nada.
        var saga = Saga(estado, Rendida(EventosCompensations.ReleaseSeatHold), Pendiente(EventosCompensations.VoidPayment));

        Assert.False(TicketPurchaseResponse.From(saga).RefundPending);
    }

    [Fact]
    public void Con_la_devolucion_hecha_no_queda_nada_por_devolver()
    {
        var saga = Saga(SagaStatus.CompensationFailed, Rendida(EventosCompensations.RestockSeats), Hecha(EventosCompensations.RefundPayment));

        Assert.False(TicketPurchaseResponse.From(saga).RefundPending);
    }

    [Theory]
    [InlineData(SagaStatus.Running)]
    [InlineData(SagaStatus.Completed)]
    public void Lo_armado_de_una_compra_que_no_se_deshace_es_el_seguro_no_un_cobro_por_devolver(SagaStatus estado)
    {
        // Una compra en curso o completada lleva su devolución ARMADA (pendiente sin haber fallado nada).
        var saga = Saga(estado, Pendiente(EventosCompensations.RefundPayment));

        Assert.False(TicketPurchaseResponse.From(saga).RefundPending);
    }
}
