using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IOrderTrackingService"/> — timelines de estados de
/// orden/expediente sobre un pipeline de etapas configurable. Seam GENÉRICO del plan doc 21 §1.4 (P4): Order ≈ Booking ≈
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
///
/// <para><b>Durabilidad (ADR 0116 fase 6).</b> El estado vive detrás de
/// <see cref="IJsonEntityStore"/> (ADR 0105) y ya no en un diccionario del
/// proceso: un reinicio borraba el timeline de envío de órdenes que seguían
/// pagadas en disco, y el comprador perdía el rastro de su compra.</para>
///
/// <para>Cada instancia necesita su propio <c>storeNamespace</c>: hay cuatro
/// —Tienda, Viajes, Educación, Eventos— con pipelines DISTINTOS, y el estado
/// guarda el índice de etapa. Compartir espacio haría que el índice de un
/// dominio se leyera contra el pipeline de otro: "enviado" convertido en
/// "matriculado" sin que nada falle.</para>
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

    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly IReadOnlyList<OrderTrackingStageDefinition> _pipeline;
    private readonly Func<DateTimeOffset> _now;
    private readonly IJsonEntityStore _store;
    private readonly string _resourceType;

    // Serializa el read-modify-write del avance. No se puede usar lock{} porque
    // el cuerpo hace await; SemaphoreSlim es el equivalente async — mismo
    // criterio que StubPaymentProvider.
    private readonly SemaphoreSlim _mutate = new(1, 1);

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
        : this(pipeline, now, null, null)
    {
    }

    /// <summary>
    /// Ctor completo (ADR 0116 fase 6): <paramref name="store"/> es el backing
    /// store durable (FileSystem en Web, InMemory en tests) y
    /// <paramref name="storeNamespace"/> lo separa del de los otros dominios.
    /// </summary>
    /// <param name="storeNamespace">Familia de entidades de ESTA instancia.
    ///   OBLIGATORIO en la práctica cuando hay store compartido: el estado
    ///   guarda el índice de etapa, y leerlo contra otro pipeline convierte
    ///   "enviado" en "matriculado" sin que nada falle.</param>
    public StubOrderTrackingService(
        IReadOnlyList<OrderTrackingStageDefinition>? pipeline,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store,
        string? storeNamespace)
    {
        if (pipeline is not null && pipeline.Count == 0)
        {
            throw new ArgumentException("El pipeline requiere al menos una etapa.", nameof(pipeline));
        }
        _pipeline = pipeline ?? ShopPipeline;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
        _resourceType = string.IsNullOrWhiteSpace(storeNamespace) ? "order-tracking" : storeNamespace;
    }

    public async Task<OrderTimeline?> GetTimelineAsync(string orderRef, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return null;
        }
        var state = await LoadAsync(orderRef.Trim(), cancellationToken).ConfigureAwait(false);
        return state is null ? null : ToTimeline(state);
    }

    public async Task<OrderTimeline> AdvanceAsync(
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
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var state = await LoadAsync(key, cancellationToken).ConfigureAwait(false)
                ?? new TimelineState(key, -1, new DateTimeOffset?[_pipeline.Count], new string?[_pipeline.Count], _now());

            // Idempotente/monotónico: etapa ya alcanzada (o anterior) → sin cambios.
            if (targetIndex <= state.CurrentIndex)
            {
                return ToTimeline(state);
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
            await _store.WriteAsync(_resourceType, key, JsonSerializer.Serialize(advanced, _json), cancellationToken)
                .ConfigureAwait(false);
            return ToTimeline(advanced);
        }
        finally
        {
            _mutate.Release();
        }
    }

    /// <summary>
    /// Lee el timeline del store. Un estado corrupto o de un pipeline con otra
    /// longitud se trata como inexistente en vez de reventar: el timeline es
    /// informativo, y perderlo no puede tumbar la consulta de una compra.
    /// </summary>
    private async Task<TimelineState?> LoadAsync(string key, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(_resourceType, key, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try
        {
            var state = JsonSerializer.Deserialize<TimelineState>(json, _json);
            if (state is null
                || state.ReachedAt.Length != _pipeline.Count
                || state.Notes.Length != _pipeline.Count)
            {
                return null;
            }
            return state;
        }
        catch (JsonException)
        {
            return null;
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
