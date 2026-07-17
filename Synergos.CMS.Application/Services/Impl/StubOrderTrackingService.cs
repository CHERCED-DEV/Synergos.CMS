using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IOrderTrackingService"/> — timelines de estados de
/// orden/expediente en memoria del proceso, sobre un pipeline de etapas
/// configurable. Seam GENÉRICO del plan doc 21 §1.4 (P4): Order ≈ Booking ≈
/// Ticket ≈ Radicado — el mismo tracking-timeline para los 8 dominios.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). El pipeline default es el de Tienda
/// (pago → preparación → envío → entrega); otros dominios construyen su
/// instancia con su propio pipeline sin tocar el contrato. Determinista e
/// idempotente/monotónico: avanzar a una etapa ya alcanzada (o anterior a la
/// actual) devuelve el timeline sin cambios; avanzar saltando etapas marca
/// las intermedias como alcanzadas (la nota queda solo en la etapa
/// destino). Agnóstico del dominio: NO valida que la orden exista — eso es
/// del dominio dueño (<c>StubShopOrderService</c> alimenta este timeline al
/// confirmar el pago). Time source inyectable para tests (ADR 0075).
/// </remarks>
public sealed class StubOrderTrackingService : IOrderTrackingService
{
    /// <summary>Etapa inicial del pipeline de Tienda — la siembra <c>StubShopOrderService</c> al confirmar.</summary>
    public const string StagePaid = "paid";

    /// <summary>Pipeline default (Tienda): pago → preparación → envío → entrega.</summary>
    public static readonly IReadOnlyList<OrderTrackingStageDefinition> ShopPipeline = new[]
    {
        new OrderTrackingStageDefinition(StagePaid, "Pago confirmado"),
        new OrderTrackingStageDefinition("preparing", "En preparación"),
        new OrderTrackingStageDefinition("shipped", "Enviado"),
        new OrderTrackingStageDefinition("delivered", "Entregado"),
    };

    private readonly IReadOnlyList<OrderTrackingStageDefinition> _pipeline;
    private readonly Func<DateTimeOffset> _now;
    private readonly ConcurrentDictionary<string, TimelineState> _timelines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public StubOrderTrackingService()
        : this(null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: <paramref name="pipeline"/> propio del dominio
    /// (null = pipeline de Tienda) + time source inyectable para determinismo
    /// en tests (null = reloj real).
    /// </summary>
    public StubOrderTrackingService(
        IReadOnlyList<OrderTrackingStageDefinition>? pipeline,
        Func<DateTimeOffset>? now)
    {
        if (pipeline is not null && pipeline.Count == 0)
        {
            throw new ArgumentException("El pipeline requiere al menos una etapa.", nameof(pipeline));
        }
        _pipeline = pipeline ?? ShopPipeline;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    public Task<OrderTimeline?> GetTimelineAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef)
            || !_timelines.TryGetValue(orderRef.Trim(), out var state))
        {
            return Task.FromResult<OrderTimeline?>(null);
        }
        return Task.FromResult<OrderTimeline?>(ToTimeline(state));
    }

    public Task<OrderTimeline> AdvanceAsync(
        string orderRef,
        string stage,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            throw new ArgumentException("La referencia de la orden es obligatoria.", nameof(orderRef));
        }
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("La etapa es obligatoria.", nameof(stage));
        }

        var targetIndex = IndexOf(stage.Trim());
        if (targetIndex < 0)
        {
            throw new ArgumentException(
                $"Etapa '{stage}' no pertenece al pipeline ({string.Join(" → ", _pipeline.Select(s => s.Stage))}).",
                nameof(stage));
        }

        var key = orderRef.Trim();
        lock (_gate)
        {
            var state = _timelines.TryGetValue(key, out var existing)
                ? existing
                : new TimelineState(key, -1, new DateTimeOffset?[_pipeline.Count], new string?[_pipeline.Count], _now());

            // Idempotente/monotónico: etapa ya alcanzada (o anterior) → sin cambios.
            if (targetIndex <= state.CurrentIndex)
            {
                return Task.FromResult(ToTimeline(state));
            }

            var at = _now();
            var reachedAt = (DateTimeOffset?[])state.ReachedAt.Clone();
            var notes = (string?[])state.Notes.Clone();
            for (var i = state.CurrentIndex + 1; i <= targetIndex; i++)
            {
                reachedAt[i] = at;
            }
            // La nota queda sellada SOLO en la etapa destino del avance.
            if (!string.IsNullOrWhiteSpace(note))
            {
                notes[targetIndex] = note.Trim();
            }

            var advanced = state with { CurrentIndex = targetIndex, ReachedAt = reachedAt, Notes = notes, UpdatedAt = at };
            _timelines[key] = advanced;
            return Task.FromResult(ToTimeline(advanced));
        }
    }

    private int IndexOf(string stage)
    {
        for (var i = 0; i < _pipeline.Count; i++)
        {
            if (string.Equals(_pipeline[i].Stage, stage, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }
        return -1;
    }

    private OrderTimeline ToTimeline(TimelineState state)
    {
        var stages = new List<OrderTimelineStage>(_pipeline.Count);
        for (var i = 0; i < _pipeline.Count; i++)
        {
            stages.Add(new OrderTimelineStage(
                Stage: _pipeline[i].Stage,
                Label: _pipeline[i].Label,
                Reached: i <= state.CurrentIndex,
                ReachedAt: state.ReachedAt[i],
                Note: state.Notes[i]));
        }
        return new OrderTimeline(
            OrderRef: state.OrderRef,
            CurrentStage: _pipeline[state.CurrentIndex].Stage,
            Stages: stages,
            UpdatedAt: state.UpdatedAt);
    }

    private sealed record TimelineState(
        string OrderRef,
        int CurrentIndex,
        DateTimeOffset?[] ReachedAt,
        string?[] Notes,
        DateTimeOffset UpdatedAt);
}
