using System.Text.Encodings.Web;
using System.Text.Json;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPaymentProvider"/> — pasarela STUB para que el motor de
/// checkout corra end-to-end en demo sin un PSP real (mismo patrón stub-first que
/// <c>StubBundleRegistryClient</c>). Auto-autoriza al crear la sesión y captura de
/// forma idempotente.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). <b>T3 (doc 25):</b> el estado ya NO vive en un
/// diccionario del proceso sino detrás del seam <see cref="IJsonEntityStore"/> —
/// con un adapter FileSystem la sesión de pago SOBREVIVE un reinicio, cerrando la
/// brecha por la que un checkout hecho antes del reinicio no se podía capturar.
/// Además expone knobs de simulación (<see cref="PaymentsSettings.DeclineTriggerSku"/>,
/// <see cref="PaymentsSettings.SimulateRequiresAction"/>) para demostrar rechazo/3DS
/// sin un PSP real. Los adapters reales (Wompi/PayU/Mercado Pago) implementan la misma
/// seam y se registran en su lugar vía el composer (Ola B) sin tocar el motor.
/// </remarks>
public sealed class StubPaymentProvider : IPaymentProvider
{
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Familia de entidades de este provider en el store genérico (→ App_Data/syn-payments/).</summary>
    private const string ResourceType = "payments";

    private readonly IJsonEntityStore _store;
    private readonly PaymentsSettings _settings;
    // Serializa el read-modify-write de Capture/Refund (single-instance). No se puede
    // usar lock{} porque el cuerpo hace await; SemaphoreSlim es el equivalente async.
    private readonly SemaphoreSlim _mutate = new(1, 1);

    /// <summary>
    /// Ctor con dependencias OPCIONALES: <paramref name="store"/> null → backing store
    /// en memoria (default de tests/efímeros); <paramref name="settings"/> null →
    /// defaults (auto-autoriza, sin knobs). El composer inyecta el store durable +
    /// settings reales (Ola A).
    /// </summary>
    public StubPaymentProvider(IJsonEntityStore? store = null, PaymentsSettings? settings = null)
    {
        _store = store ?? new InMemoryJsonEntityStore();
        _settings = settings ?? new PaymentsSettings();
    }

    public string ProviderKey => "stub";

    public async Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Amount <= 0m)
        {
            throw new ArgumentException("El monto del pago debe ser mayor a cero.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            throw new ArgumentException("La moneda es obligatoria.", nameof(request));
        }
        if (request.Items is null || request.Items.Count == 0)
        {
            throw new ArgumentException("La sesión de pago requiere al menos una línea.", nameof(request));
        }

        var sessionId = $"stub_{Guid.NewGuid():N}";

        // Knob de simulación: un SKU "veneno" declina la sesión (rechazo limpio, demo).
        if (!string.IsNullOrWhiteSpace(_settings.DeclineTriggerSku)
            && request.Items.Any(i => string.Equals(i.Sku, _settings.DeclineTriggerSku, StringComparison.OrdinalIgnoreCase)))
        {
            await WriteSessionAsync(sessionId, new PersistedSession(PaymentStatus.Failed, request.Amount, 0m, request.OrderReference, null), cancellationToken);
            return new PaymentSession(sessionId, PaymentStatus.Failed, Action: null, ProviderKey);
        }

        // Knob de simulación: RequiresAction + RedirectUrl sintética (simula 3DS/redirect
        // a hosted checkout). El cliente "vuelve" y el webhook confirma (Ola B lo cablea
        // al front; en Ola A demuestra que el stub modela el estado).
        if (_settings.SimulateRequiresAction)
        {
            // Simula la forma de acción que le tocaría al riel pedido, para que
            // el front se pueda construir contra los TRES modos antes de que
            // exista Wompi. Sin esto solo se podía probar el redirect.
            var redirect = $"/checkout/simulated-3ds/{sessionId}";
            var action = (request.PreferredMethod ?? "card").ToLowerInvariant() switch
            {
                "pse" or "bancolombia" => PaymentAction.Redirect(new Uri(redirect, UriKind.Relative)),
                "nequi" => PaymentAction.AwaitApproval("Aprobá la compra en tu app Nequi."),
                _ => PaymentAction.Challenge($"<!doctype html><p>Reto 3DS simulado para {sessionId}</p>"),
            };
            await WriteSessionAsync(sessionId, new PersistedSession(PaymentStatus.RequiresAction, request.Amount, 0m, request.OrderReference, redirect), cancellationToken);
            return new PaymentSession(sessionId, PaymentStatus.RequiresAction, action, ProviderKey);
        }

        // Default: auto-autoriza (fondos "reservados"); un PSP real podría nacer PENDING.
        await WriteSessionAsync(sessionId, new PersistedSession(PaymentStatus.Authorized, request.Amount, 0m, request.OrderReference, null), cancellationToken);
        return new PaymentSession(sessionId, PaymentStatus.Authorized, Action: null, ProviderKey);
    }

    public async Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var s = await LoadSessionAsync(sessionId, cancellationToken);
        return s is null
            ? NotFound(sessionId)
            // Lo devuelto viaja en TODA respuesta sobre la sesión, no sólo en la de devolver:
            // es un dato de la sesión, y quien consulta necesita poder ver que ya salió plata.
            : new PaymentOutcome(sessionId, s.Status, s.Captured, null, s.Refunded);
    }

    public async Task<PaymentOutcome> CaptureAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return NotFound(sessionId);
        }

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var s = await LoadSessionAsync(sessionId, cancellationToken);
            if (s is null)
            {
                return NotFound(sessionId);
            }
            // Idempotente: capturar dos veces deja el mismo estado/monto, sin doble cobro.
            if (s.Status is PaymentStatus.Authorized or PaymentStatus.Captured)
            {
                // Captura parcial: nunca por encima de lo autorizado, y la
                // segunda llamada no vuelve a sumar (idempotente por diseño —
                // un reintento del webhook no puede cobrar dos veces).
                var target = amount is decimal a && a > 0m ? Math.Min(a, s.Amount) : s.Amount;
                var captured = s with { Status = PaymentStatus.Captured, Captured = target };
                await WriteSessionAsync(sessionId, captured, cancellationToken);
                return new PaymentOutcome(sessionId, PaymentStatus.Captured, captured.Captured, null, captured.Refunded);
            }
            return new PaymentOutcome(sessionId, s.Status, s.Captured, $"No se puede capturar en estado {s.Status}.", s.Refunded);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<PaymentOutcome> VoidAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return NotFound(sessionId);
        }

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var s = await LoadSessionAsync(sessionId, cancellationToken);
            if (s is null)
            {
                return NotFound(sessionId);
            }
            // Idempotente: anular lo ya anulado no es un error.
            if (s.Status == PaymentStatus.Cancelled)
            {
                return new PaymentOutcome(sessionId, PaymentStatus.Cancelled, 0m);
            }
            // Solo se anula lo que NO se cobró. Lo capturado se devuelve con
            // RefundAsync — son hechos distintos y confundirlos deja el libro
            // contable sin forma de saber si hubo cobro.
            if (s.Status is not (PaymentStatus.Authorized or PaymentStatus.Pending or PaymentStatus.RequiresAction))
            {
                return new PaymentOutcome(sessionId, s.Status, s.Captured,
                    $"Solo se anula lo no capturado (estado actual {s.Status}).");
            }
            await WriteSessionAsync(sessionId, s with { Status = PaymentStatus.Cancelled, Captured = 0m }, cancellationToken);
            return new PaymentOutcome(sessionId, PaymentStatus.Cancelled, 0m);
        }
        finally
        {
            _mutate.Release();
        }
    }

    public async Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId))
        {
            return NotFound(sessionId);
        }

        await _mutate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var s = await LoadSessionAsync(sessionId, cancellationToken);
            if (s is null)
            {
                return NotFound(sessionId);
            }
            // Captured o Refunded: una sesión con una devolución PARCIAL sigue teniendo saldo, y
            // rechazarla por «ya está reembolsada» dejaba la segunda línea de un pedido sin poder
            // devolverse (#57).
            if (s.Status is not (PaymentStatus.Captured or PaymentStatus.Refunded))
            {
                return new PaymentOutcome(sessionId, s.Status, s.Captured,
                    $"Solo se reembolsa lo capturado (estado actual {s.Status}).", s.Refunded);
            }

            // EL MONTO SE RESPETA. Antes se ignoraba y la sesión entera pasaba a Refunded, así
            // que devolver una línea de tres marcaba el pedido completo como reembolsado —y la
            // siguiente devolución se rechazaba por un estado que nadie había querido poner.
            var pedido = amount ?? (s.Captured - s.Refunded);
            if (pedido <= 0m)
            {
                return new PaymentOutcome(sessionId, s.Status, s.Captured,
                    $"No se puede devolver {pedido}.", s.Refunded);
            }

            var disponible = s.Captured - s.Refunded;
            if (pedido > disponible)
            {
                return new PaymentOutcome(sessionId, s.Status, s.Captured,
                    $"Se pidió devolver {pedido} y quedan {disponible} por devolver.", s.Refunded);
            }

            var acumulado = s.Refunded + pedido;

            // El estado sólo cambia cuando ya no queda nada. Con saldo, la sesión sigue
            // CAPTURADA con una devolución parcial anotada, que es lo que de verdad pasó.
            var estado = acumulado >= s.Captured ? PaymentStatus.Refunded : PaymentStatus.Captured;

            await WriteSessionAsync(sessionId, s with { Status = estado, Refunded = acumulado }, cancellationToken);
            return new PaymentOutcome(sessionId, estado, s.Captured, null, acumulado);
        }
        finally
        {
            _mutate.Release();
        }
    }

    private async Task<PersistedSession?> LoadSessionAsync(string sessionId, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(ResourceType, sessionId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }
        try { return JsonSerializer.Deserialize<PersistedSession>(json, _json); }
        catch (JsonException) { return null; }   // corrupto → como si no existiera
    }

    private Task WriteSessionAsync(string sessionId, PersistedSession session, CancellationToken cancellationToken)
        => _store.WriteAsync(ResourceType, sessionId, JsonSerializer.Serialize(session, _json), cancellationToken);

    private static PaymentOutcome NotFound(string? sessionId)
        => new(sessionId ?? string.Empty, PaymentStatus.Failed, 0m, "Sesión de pago no encontrada.");

    /// <summary>Estado serializado de la sesión: monto/estado/capturado + la orden asociada.</summary>
    /// <remarks>
    /// <c>Refunded</c> lleva default para que las sesiones ya escritas en disco sigan
    /// deserializando: la que no lo trae es una en la que nadie devolvió nada, que es la verdad.
    /// </remarks>
    private sealed record PersistedSession(
        PaymentStatus Status, decimal Amount, decimal Captured, string? OrderRef, string? RedirectUrl,
        decimal Refunded = 0m);
}
