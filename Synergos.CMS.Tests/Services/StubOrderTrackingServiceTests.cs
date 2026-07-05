using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubOrderTrackingService"/> (seam GENÉRICO
/// <see cref="IOrderTrackingService"/>, plan doc 21 §1.4 P4 — timeline de
/// estados de orden/expediente, Order≈Booking≈Ticket≈Radicado): los 4 casos
/// canónicos (ADR 0075) — empty / happy / filter / idempotent — más pipeline
/// custom por dominio y monotonicidad.
/// </summary>
public class StubOrderTrackingServiceTests
{
    private static IOrderTrackingService Make(Func<DateTimeOffset>? now = null)
        => new StubOrderTrackingService(null, now);

    [Fact] // empty: orden sin tracking iniciado (o ref vacío) → null
    public async Task GetTimeline_Unknown_ReturnsNull()
    {
        var svc = Make();
        Assert.Null(await svc.GetTimelineAsync("ord_desconocida"));
        Assert.Null(await svc.GetTimelineAsync(""));
    }

    [Fact] // inválido: ref vacío o etapa fuera del pipeline lanzan
    public async Task Advance_InvalidArguments_Throw()
    {
        var svc = Make();
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AdvanceAsync("", "paid"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AdvanceAsync("ord_1", ""));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AdvanceAsync("ord_1", "no-es-etapa"));
    }

    [Fact] // happy: primer avance crea el timeline con las 4 etapas del pipeline de Tienda
    public async Task Advance_FirstStage_CreatesTimeline()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        var timeline = await svc.AdvanceAsync("ord_1", "paid", "Pago capturado.");

        Assert.Equal("ord_1", timeline.OrderRef);
        Assert.Equal("paid", timeline.CurrentStage);
        Assert.Equal(4, timeline.Stages.Count);
        Assert.Equal(new[] { "paid", "preparing", "shipped", "delivered" },
            timeline.Stages.Select(s => s.Stage).ToArray());

        var paid = timeline.Stages[0];
        Assert.True(paid.Reached);
        Assert.Equal(clock, paid.ReachedAt);
        Assert.Equal("Pago capturado.", paid.Note);
        // Las siguientes siguen pendientes.
        Assert.All(timeline.Stages.Skip(1), s => Assert.False(s.Reached));

        // GetTimeline devuelve lo mismo.
        var read = await svc.GetTimelineAsync("ord_1");
        Assert.NotNull(read);
        Assert.Equal("paid", read!.CurrentStage);
    }

    [Fact] // happy: saltar etapas marca las intermedias; la nota queda solo en la destino
    public async Task Advance_SkippingStages_MarksIntermediates()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        await svc.AdvanceAsync("ord_1", "paid");
        clock = clock.AddHours(6);
        var timeline = await svc.AdvanceAsync("ord_1", "shipped", "Guía TCC 12345.");

        Assert.Equal("shipped", timeline.CurrentStage);
        Assert.True(timeline.Stages[1].Reached);          // preparing — intermedia
        Assert.Equal(clock, timeline.Stages[1].ReachedAt);
        Assert.Null(timeline.Stages[1].Note);              // nota solo en la destino
        Assert.Equal("Guía TCC 12345.", timeline.Stages[2].Note);
        Assert.False(timeline.Stages[3].Reached);          // delivered pendiente
    }

    [Fact] // filter: cada orden tiene su propio timeline aislado
    public async Task Timelines_AreScopedPerOrder()
    {
        var svc = Make();
        await svc.AdvanceAsync("ord_a", "shipped");
        await svc.AdvanceAsync("ord_b", "paid");

        Assert.Equal("shipped", (await svc.GetTimelineAsync("ord_a"))!.CurrentStage);
        Assert.Equal("paid", (await svc.GetTimelineAsync("ord_b"))!.CurrentStage);
        Assert.Null(await svc.GetTimelineAsync("ord_c"));
    }

    [Fact] // idempotent: re-avanzar a la etapa actual (o a una anterior) no cambia nada
    public async Task Advance_SameOrEarlierStage_IsIdempotent()
    {
        var clock = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
        var svc = Make(() => clock);

        await svc.AdvanceAsync("ord_1", "paid");
        clock = clock.AddHours(1);
        var first = await svc.AdvanceAsync("ord_1", "preparing", "En bodega.");
        clock = clock.AddHours(1);

        // Re-avanzar a la etapa actual → sin cambios (misma fecha, misma nota).
        var again = await svc.AdvanceAsync("ord_1", "preparing", "Otra nota que NO debe pisar.");
        Assert.Equal(first.UpdatedAt, again.UpdatedAt);
        Assert.Equal("En bodega.", again.Stages[1].Note);

        // Retroceder (etapa anterior) → también no-op monotónico.
        var back = await svc.AdvanceAsync("ord_1", "paid");
        Assert.Equal("preparing", back.CurrentStage);
    }

    [Fact] // genérico: otro dominio construye su instancia con su propio pipeline
    public async Task CustomPipeline_OtherDomain_Works()
    {
        var pipeline = new[]
        {
            new OrderTrackingStageDefinition("radicado", "Radicado"),
            new OrderTrackingStageDefinition("en-revision", "En revisión"),
            new OrderTrackingStageDefinition("resuelto", "Resuelto"),
        };
        var svc = new StubOrderTrackingService(pipeline, null);

        var timeline = await svc.AdvanceAsync("exp-2026-001", "en-revision");

        Assert.Equal("en-revision", timeline.CurrentStage);
        Assert.Equal(3, timeline.Stages.Count);
        // La etapa de Tienda no existe en este pipeline.
        await Assert.ThrowsAsync<ArgumentException>(() => svc.AdvanceAsync("exp-2026-001", "shipped"));
    }
}
