using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IReturnService"/> — devoluciones/reclamos (RMA) del
/// marketplace en memoria del proceso. Compone los seams existentes:
/// <see cref="IShopOrderService"/> resuelve la orden/línea real
/// (anti-tampering — el monto a reembolsar sale de la orden, no del cliente),
/// <see cref="IPaymentProvider.RefundAsync"/> ejecuta el reembolso al llegar
/// a <see cref="ShopReturnStatus.Refunded"/>, y <see cref="IAuditTrailWriter"/>
/// deja rastro forense append-only de cada solicitud y transición (ADR 0037).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Máquina de estados legal (mismo patrón que
/// <c>StubCaseWorkflowService</c> de Gobierno): Requested→Approved|Rejected ·
/// Approved→Received · Received→Refunded; Rejected/Refunded terminales.
/// <see cref="RequestAsync"/> es idempotente por (orden + línea): re-pedir
/// devuelve el caso abierto existente (solo un rechazo previo habilita un
/// caso nuevo). <see cref="AdvanceAsync"/> es idempotente al re-transicionar
/// al estado actual — sin doble reembolso. Estado en memoria (proceso),
/// suficiente para demo; un adapter real delega a OMS/mediación. Time source
/// inyectable para determinismo en tests (ADR 0075).
/// </remarks>
public sealed class StubReturnService : IReturnService
{
    private static readonly IReadOnlyDictionary<ShopReturnStatus, ShopReturnStatus[]> LegalTransitions =
        new Dictionary<ShopReturnStatus, ShopReturnStatus[]>
        {
            [ShopReturnStatus.Requested] = new[] { ShopReturnStatus.Approved, ShopReturnStatus.Rejected },
            [ShopReturnStatus.Approved] = new[] { ShopReturnStatus.Received },
            [ShopReturnStatus.Received] = new[] { ShopReturnStatus.Refunded },
            [ShopReturnStatus.Rejected] = Array.Empty<ShopReturnStatus>(),
            [ShopReturnStatus.Refunded] = Array.Empty<ShopReturnStatus>(),
        };

    private readonly IShopOrderService _orders;
    private readonly IPaymentProvider _payments;
    private readonly IAuditTrailWriter? _audit;
    private readonly Func<DateTimeOffset> _now;
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Familia de entidades en el store genérico (→ App_Data/syn-returns/).</summary>
    private const string ResourceType = "returns";

    private readonly IJsonEntityStore _store;

    // Serializa el read-modify-write. lock{} no sirve: el cuerpo hace await.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    public StubReturnService(IShopOrderService orders, IPaymentProvider payments)
        : this(orders, payments, null, null)
    {
    }

    /// <summary>
    /// Ctor con audit opcional (null = sin rastro, e.g. tests unitarios) y
    /// time source inyectable para determinismo en tests. Null = reloj real.
    /// </summary>
    public StubReturnService(
        IShopOrderService orders,
        IPaymentProvider payments,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now)
        : this(orders, payments, audit, now, null)
    {
    }

    /// <summary>
    /// Ctor completo (ADR 0116 fase 6): <paramref name="store"/> es el backing
    /// store durable.
    /// </summary>
    /// <remarks>
    /// El estado vivía en un diccionario del proceso mientras órdenes y pagos ya
    /// estaban en disco. Un reinicio borraba los RMA abiertos de órdenes que
    /// seguían "Paid": el comprador había pedido su devolución y para el sistema
    /// no había pasado nada.
    /// </remarks>
    public StubReturnService(
        IShopOrderService orders,
        IPaymentProvider payments,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now,
        IJsonEntityStore? store)
    {
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _payments = payments ?? throw new ArgumentNullException(nameof(payments));
        _audit = audit;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _store = store ?? new InMemoryJsonEntityStore();
    }

    /// <summary>Lee TODOS los RMA del store, saltando lo corrupto.</summary>
    private async Task<List<ShopReturnCase>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var raws = await _store.ListAsync(ResourceType, cancellationToken).ConfigureAwait(false);
        var cases = new List<ShopReturnCase>(raws.Count);
        foreach (var json in raws)
        {
            if (string.IsNullOrWhiteSpace(json)) continue;
            try
            {
                var rma = JsonSerializer.Deserialize<ShopReturnCase>(json, _json);
                if (rma is not null) cases.Add(rma);
            }
            catch (JsonException) { /* un RMA corrupto no puede tumbar la lista */ }
        }
        return cases;
    }

    private async Task<ShopReturnCase?> LoadAsync(string rmaId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, rmaId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ShopReturnCase>(json, _json); }
        catch (JsonException) { return null; }
    }

    private Task SaveAsync(ShopReturnCase rma, CancellationToken cancellationToken)
        => _store.WriteAsync(ResourceType, rma.RmaId, JsonSerializer.Serialize(rma, _json), cancellationToken);

    public async Task<ShopReturnCase> RequestAsync(
        string orderRef,
        string lineId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            throw new ArgumentException("La referencia de la orden es obligatoria.", nameof(orderRef));
        }
        if (string.IsNullOrWhiteSpace(lineId))
        {
            throw new ArgumentException("La línea a devolver es obligatoria.", nameof(lineId));
        }
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException("El motivo de la devolución es obligatorio.", nameof(reason));
        }

        var order = await _orders.GetOrderAsync(orderRef.Trim(), cancellationToken)
            ?? throw new ArgumentException("Orden no encontrada.", nameof(orderRef));
        if (order.Status != OrderStatus.Paid)
        {
            throw new ArgumentException(
                $"Solo se puede devolver sobre una orden pagada (estado actual {order.Status}).", nameof(orderRef));
        }

        var line = ResolveLine(order, lineId.Trim())
            ?? throw new ArgumentException(
                $"La línea '{lineId}' no está en la orden {order.OrderRef}.", nameof(lineId));
        var lineRef = string.IsNullOrWhiteSpace(line.VariantId)
            ? line.ProductId
            : $"{line.ProductId}/{line.VariantId}";

        ShopReturnCase rma;
        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Idempotente por (orden + línea): un caso NO rechazado ya abierto
            // se devuelve tal cual — no se duplican RMAs de la misma línea.
            var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
            var existing = all.FirstOrDefault(c =>
                string.Equals(c.OrderRef, order.OrderRef, StringComparison.Ordinal)
                && string.Equals(c.LineRef, lineRef, StringComparison.OrdinalIgnoreCase)
                && c.Status != ShopReturnStatus.Rejected);
            if (existing is not null)
            {
                return existing;
            }

            var at = _now();
            rma = new ShopReturnCase(
                RmaId: $"rma_{Guid.NewGuid():N}",
                OrderRef: order.OrderRef,
                LineRef: lineRef,
                ProductName: line.ProductName,
                Quantity: line.Quantity,
                RefundAmount: line.LineTotal,
                Currency: line.Currency,
                Reason: reason.Trim(),
                Status: ShopReturnStatus.Requested,
                RequestedAt: at,
                UpdatedAt: at,
                Note: null);
            await SaveAsync(rma, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _mutate.Release();
        }

        await AuditAsync(
            id: rma.RmaId,
            actorEmail: order.CustomerEmail,
            actorName: order.CustomerName,
            action: "shop.return-requested",
            resource: $"{rma.OrderRef}/{rma.LineRef}",
            detail: $"RMA {rma.RmaId} solicitado: {rma.Reason}",
            cancellationToken);

        return rma;
    }

    public async Task<ShopReturnCase> AdvanceAsync(
        string rmaId,
        ShopReturnStatus target,
        string? note = null,
        CancellationToken cancellationToken = default)
    {
        var rma = string.IsNullOrWhiteSpace(rmaId)
            ? null
            : await LoadAsync(rmaId.Trim(), cancellationToken).ConfigureAwait(false);
        if (rma is null)
        {
            throw new ArgumentException("RMA no encontrado.", nameof(rmaId));
        }

        // Idempotente: re-transicionar al estado actual → sin cambios ni doble efecto.
        if (rma.Status == target)
        {
            return rma;
        }

        if (!LegalTransitions[rma.Status].Contains(target))
        {
            throw new InvalidOperationException(
                $"Transición ilegal del RMA {rma.RmaId}: {rma.Status} → {target}.");
        }

        // Reembolso al llegar a Refunded — vía la sesión de pago de la orden.
        if (target == ShopReturnStatus.Refunded)
        {
            var order = await _orders.GetOrderAsync(rma.OrderRef, cancellationToken)
                ?? throw new InvalidOperationException($"La orden {rma.OrderRef} del RMA ya no existe.");
            if (string.IsNullOrWhiteSpace(order.PaymentSessionId))
            {
                throw new InvalidOperationException($"La orden {rma.OrderRef} no tiene sesión de pago para reembolsar.");
            }

            var outcome = await _payments.RefundAsync(order.PaymentSessionId, rma.RefundAmount, cancellationToken);
            if (outcome.Status != PaymentStatus.Refunded)
            {
                throw new InvalidOperationException(
                    outcome.FailureReason ?? $"No se pudo reembolsar el RMA (estado del pago {outcome.Status}).");
            }
        }

        var advanced = rma with
        {
            Status = target,
            UpdatedAt = _now(),
            Note = string.IsNullOrWhiteSpace(note) ? rma.Note : note.Trim(),
        };
        await SaveAsync(advanced, cancellationToken).ConfigureAwait(false);

        await AuditAsync(
            id: $"{advanced.RmaId}:{target}".ToLowerInvariant(),
            actorEmail: string.Empty, // transición del vendedor/plataforma — actor del sistema en el stub
            actorName: "Tienda",
            action: "shop.return-transition",
            resource: $"{advanced.OrderRef}/{advanced.LineRef}",
            detail: $"RMA {advanced.RmaId}: {rma.Status} → {target}."
                + (string.IsNullOrWhiteSpace(note) ? string.Empty : $" Nota: {note.Trim()}"),
            cancellationToken);

        return advanced;
    }

    public async Task<IReadOnlyList<ShopReturnCase>> GetForOrderAsync(
        string orderRef,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(orderRef))
        {
            return Array.Empty<ShopReturnCase>();
        }

        var key = orderRef.Trim();
        var all = await LoadAllAsync(cancellationToken).ConfigureAwait(false);
        return all
            .Where(c => string.Equals(c.OrderRef, key, StringComparison.Ordinal))
            .OrderByDescending(c => c.RequestedAt)
            .ThenBy(c => c.RmaId, StringComparer.Ordinal)
            .ToList();
    }

    private static ShopOrderLine? ResolveLine(ShopOrder order, string lineId)
        => order.Lines.FirstOrDefault(l =>
               string.Equals($"{l.ProductId}/{l.VariantId}", lineId, StringComparison.OrdinalIgnoreCase))
           ?? order.Lines.FirstOrDefault(l =>
               string.Equals(l.ProductId, lineId, StringComparison.OrdinalIgnoreCase));

    private async Task AuditAsync(
        string id,
        string actorEmail,
        string actorName,
        string action,
        string resource,
        string detail,
        CancellationToken cancellationToken)
    {
        if (_audit is null)
        {
            return;
        }
        // best-effort: el RMA YA está persistido.
        await BestEffort.RunAsync(() => _audit.WriteAsync(
                new AuditEvent(
                    Id: id,
                    OccurredAtUtc: _now().UtcDateTime,
                    ActorEmail: actorEmail,
                    ActorName: actorName,
                    Action: action,
                    Resource: resource,
                    Outcome: "success",
                    Detail: detail),
                cancellationToken), cancellationToken);
    }
}
