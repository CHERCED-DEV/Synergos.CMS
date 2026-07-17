using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// El dispatcher de notificaciones transaccionales (T4, doc 25) — la ÚNICA impl de
/// <see cref="ITransactionalNotifier"/>. Abanica a los canales registrados
/// (<see cref="ITransactionalNotifierChannel"/>), calcando el patrón ya vivo de
/// <see cref="CompositeAlertNotifier"/>. Es también el ÚNICO dueño del gating y de la
/// idempotencia: replicarlos en los 6 motores sería la duplicación por-dominio que la
/// regla de oro del doc 25 prohíbe.
/// </summary>
/// <remarks>
/// <b>Es TOTAL: DispatchAsync nunca lanza.</b> Todo el cuerpo va en try/catch — un email
/// caído jamás puede tumbar una orden ya pagada. (El motor igual envuelve con
/// <c>NotificationEmission.SafeDispatchAsync</c>: la garantía es estructural, no confiada.)
///
/// <para><b>IDEMPOTENCIA — se marca ANTES de enviar (at-most-once). Es una elección
/// irreducible, no un descuido:</b> con un archivo y un email no existe exactly-once.
/// Marcar DESPUÉS sería at-least-once y no protegería de la carrera, que aquí es la
/// condición DISEÑADA: el comprador vuelve del redirect y llama <c>/confirm</c> mientras el
/// PSP postea el webhook — ambos confirman la misma orden y ambos emiten. Un recibo
/// DUPLICADO no tiene mitigación (no se des-envía un email); uno PERDIDO sí la tiene (el
/// historial durable de T1/T2 y el re-envío al re-confirmar). Ver ADR.</para>
/// </remarks>
public sealed class CompositeTransactionalNotifier : ITransactionalNotifier
{
    /// <summary>Familia del ledger → App_Data/syn-notifications/. Scope PROPIO: compartir el del webhook rompería su semántica de reintentos.</summary>
    private const string LedgerScope = "notifications";

    private readonly IEnumerable<ITransactionalNotifierChannel> _channels;
    private readonly IIdempotencyLedger _ledger;
    private readonly IAuditTrailWriter _audit;
    private readonly IOptionsMonitor<NotificationsSettings> _settings;
    private readonly ILogger<CompositeTransactionalNotifier> _logger;

    public CompositeTransactionalNotifier(
        IEnumerable<ITransactionalNotifierChannel> channels,
        IIdempotencyLedger ledger,
        IAuditTrailWriter audit,
        IOptionsMonitor<NotificationsSettings> settings,
        ILogger<CompositeTransactionalNotifier> logger)
    {
        _channels = channels;
        _ledger = ledger;
        _audit = audit;
        _settings = settings;
        _logger = logger;
    }

    public async Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
    {
        try
        {
            if (notification is null)
            {
                return;
            }

            // 1) Gates ANTES del claim — apagado no debe quemar la clave (si no, al
            //    encender, el hecho ya reclamado jamás notificaría).
            var settings = _settings.CurrentValue;
            if (!settings.Enabled || settings.DisabledTypes.Contains(notification.Type))
            {
                return;
            }
            if (string.IsNullOrWhiteSpace(notification.ToEmail) || !notification.ToEmail.Contains('@'))
            {
                _logger.LogInformation(
                    "Notificación {Type} sin destinatario válido — omitida (subject={SubjectId}).",
                    notification.Type, notification.SubjectId);
                return;
            }

            // 2) IDEMPOTENCIA: un hecho → un aviso. Marca ANTES de enviar (at-most-once).
            if (!await _ledger.TryClaimAsync(LedgerScope, notification.ResolvedDedupeKey, cancellationToken))
            {
                await AuditAsync(notification, "suppressed", "Duplicado: el hecho ya fue notificado.", cancellationToken);
                return;
            }

            // 3) Fan-out con aislamiento POR canal (un canal caído no tumba a los otros).
            var failed = 0;
            var attempted = 0;
            foreach (var channel in _channels)
            {
                attempted++;
                try
                {
                    await channel.DispatchAsync(notification, cancellationToken);
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogWarning(ex,
                        "Canal de notificación falló: channel={Channel} type={Type} subject={SubjectId}",
                        channel.GetType().Name, notification.Type, notification.SubjectId);
                }
            }

            await AuditAsync(
                notification,
                failed == 0 ? "dispatched" : "failed",
                failed == 0
                    ? $"Notificado por {attempted} canal(es)."
                    : $"{failed}/{attempted} canal(es) fallaron.",
                cancellationToken);
        }
        catch (Exception ex)
        {
            // TOTAL: ni un fallo del ledger, del audit o de los settings puede propagar.
            _logger.LogWarning(ex,
                "Dispatch de notificación falló por completo: type={Type}",
                notification?.Type);
        }
    }

    // Rastro forense de qué se avisó, qué se suprimió por duplicado y qué falló — con la
    // clave de dedupe, que es lo que permite auditar el "no le llegó el recibo".
    private async Task AuditAsync(NotificationEvent n, string outcome, string detail, CancellationToken ct)
    {
        try
        {
            await _audit.WriteAsync(
                new AuditEvent(
                    Id: Guid.NewGuid().ToString("N"),
                    OccurredAtUtc: DateTime.UtcNow,
                    ActorEmail: n.ToEmail,
                    ActorName: n.ToName,
                    Action: "notification." + outcome,
                    Resource: n.ResolvedDedupeKey,
                    Outcome: outcome == "failed" ? "failure" : "success",
                    Detail: detail),
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo auditar la notificación {Key}", n.ResolvedDedupeKey);
        }
    }
}
