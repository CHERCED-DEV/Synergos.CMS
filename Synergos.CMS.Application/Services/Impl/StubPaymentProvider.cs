using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="IPaymentProvider"/> — pasarela STUB para que el motor
/// de checkout corra end-to-end en demo sin un PSP real (mismo patrón
/// stub-first que <c>StubBundleRegistryClient</c>). Auto-autoriza al crear
/// la sesión y captura de forma idempotente, en memoria.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). Los adapters reales (Stripe / Wompi /
/// PayU CO) implementan la misma seam y se registran en su lugar vía el
/// composer cuando el arquitecto elija el PSP — sin tocar el motor.
/// El estado vive en memoria (proceso), suficiente para demo; un adapter
/// real delega el estado al PSP.
/// </remarks>
public sealed class StubPaymentProvider : IPaymentProvider
{
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public string ProviderKey => "stub";

    public Task<PaymentSession> CreateSessionAsync(PaymentSessionRequest request, CancellationToken cancellationToken = default)
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
        // El stub auto-autoriza (fondos "reservados"); un PSP real podría
        // devolver RequiresAction con RedirectUrl para 3DS.
        _sessions[sessionId] = new Session(PaymentStatus.Authorized, request.Amount, 0m);
        return Task.FromResult(new PaymentSession(sessionId, PaymentStatus.Authorized, RedirectUrl: null, ClientSecret: null, ProviderKey));
    }

    public Task<PaymentOutcome> GetStatusAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult(_sessions.TryGetValue(sessionId ?? string.Empty, out var s)
            ? new PaymentOutcome(sessionId!, s.Status, s.Captured)
            : NotFound(sessionId));

    public Task<PaymentOutcome> CaptureAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId) || !_sessions.TryGetValue(sessionId, out var s))
        {
            return Task.FromResult(NotFound(sessionId));
        }
        // Idempotente: capturar dos veces deja el mismo estado/monto, sin doble cobro.
        if (s.Status is PaymentStatus.Authorized or PaymentStatus.Captured)
        {
            var captured = new Session(PaymentStatus.Captured, s.Amount, s.Amount);
            _sessions[sessionId] = captured;
            return Task.FromResult(new PaymentOutcome(sessionId, PaymentStatus.Captured, captured.Captured));
        }
        return Task.FromResult(new PaymentOutcome(sessionId, s.Status, s.Captured, $"No se puede capturar en estado {s.Status}."));
    }

    public Task<PaymentOutcome> RefundAsync(string sessionId, decimal? amount = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId) || !_sessions.TryGetValue(sessionId, out var s))
        {
            return Task.FromResult(NotFound(sessionId));
        }
        if (s.Status != PaymentStatus.Captured)
        {
            return Task.FromResult(new PaymentOutcome(sessionId, s.Status, s.Captured, $"Solo se reembolsa lo capturado (estado actual {s.Status})."));
        }
        _sessions[sessionId] = new Session(PaymentStatus.Refunded, s.Amount, s.Captured);
        return Task.FromResult(new PaymentOutcome(sessionId, PaymentStatus.Refunded, s.Captured));
    }

    private static PaymentOutcome NotFound(string? sessionId)
        => new(sessionId ?? string.Empty, PaymentStatus.Failed, 0m, "Sesión de pago no encontrada.");

    private readonly record struct Session(PaymentStatus Status, decimal Amount, decimal Captured);
}
