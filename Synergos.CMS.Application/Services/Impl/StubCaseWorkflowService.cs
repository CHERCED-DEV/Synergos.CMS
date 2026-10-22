using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICaseWorkflowService"/> — máquina de estados STUB del expediente
/// del vertical Gobierno (doc gobierno.md §4, cara de funcionario). Resuelve cada acción
/// (<c>take | approve | reject | request-info</c>) a la transición correspondiente, valida que
/// sea legal (tabla explícita, State pattern), es IDEMPOTENTE sobre el estado destino y asienta
/// CADA acción legal como evento append-only en <see cref="IAuditTrailWriter"/> — el rastro
/// forense es el diferenciador del dominio.
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica estado: muta el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP, mismo patrón
/// <c>StubEventManagementService</c> → <c>StubEventTicketingService</c>). Aplicar una acción
/// cuyo estado destino YA es el actual es idempotente (no re-audita ni toca el historial). Los
/// estados terminales (Resuelto/Rechazado) no admiten nuevas acciones ⇒
/// <see cref="InvalidOperationException"/>. ADR 0075 (idempotent es el caso central).
/// </remarks>
public sealed class StubCaseWorkflowService : ICaseWorkflowService
{
    // acción → (estado destino, estados de origen legales).
    //
    // ES LA v2 DEL PROCESO (`gov.tramite.v2`, tools/provisionar.procesos.json), y
    // ProcesoDelTramiteTests la cruza par por par con ella. Medido el 2026-10-16: esta tabla y la
    // definición publicada diferían en 3 de 9 pares —con la capacidad conectada no se podía
    // rechazar después de pedir información— y nada lo veía (D-3 de 009). Tomar antes de decidir
    // (P4) y la subsanación que vuelve a revisión (P8) son de la v2.
    private static readonly IReadOnlyDictionary<string, (CaseStatus To, CaseStatus[] From)> Transiciones =
        new Dictionary<string, (CaseStatus, CaseStatus[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["take"] = (CaseStatus.EnRevision, new[] { CaseStatus.Radicado }),
            ["approve"] = (CaseStatus.Resuelto, new[] { CaseStatus.EnRevision }),
            ["reject"] = (CaseStatus.Rechazado, new[] { CaseStatus.EnRevision }),
            ["request-info"] = (CaseStatus.Subsanacion, new[] { CaseStatus.EnRevision }),
            ["resubmit"] = (CaseStatus.EnRevision, new[] { CaseStatus.Subsanacion }),
        };

    /// <summary>Las acciones que <see cref="DecideAsync"/> admite: las decisiones del funcionario.</summary>
    private static readonly string[] Desenlaces = { "approve", "reject", "request-info" };

    private readonly GovCaseDecisionRecorder _recorder;
    private readonly Func<DateTimeOffset> _now;

    public StubCaseWorkflowService(StubApplicationService cases)
        : this(cases, null, null)
    {
    }

    /// <summary>
    /// Ctor configurable: audit opcional (null = no-op, tests aislados) + time source
    /// inyectable para determinismo en tests (ADR 0002) + notifier opcional (T4) ÚLTIMO:
    /// null = no notificar (comportamiento pre-T4), cero call-sites rotos.
    /// </summary>
    public StubCaseWorkflowService(
        StubApplicationService cases,
        IAuditTrailWriter? audit,
        Func<DateTimeOffset>? now,
        ITransactionalNotifier? notifier = null)
    {
        _recorder = new GovCaseDecisionRecorder(cases, audit, notifier);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// La tabla, para el gate que la cruza con la definición publicada (FR-007 de 009). Sólo lectura.
    /// </summary>
    public static IReadOnlyDictionary<string, (CaseStatus To, IReadOnlyList<CaseStatus> From)> Tabla
        => Transiciones.ToDictionary(
            t => t.Key,
            t => (t.Value.To, (IReadOnlyList<CaseStatus>)t.Value.From),
            StringComparer.OrdinalIgnoreCase);

    public Task<CaseDetail> DecideAsync(
        string caseId,
        string outcome,
        string note,
        CaseActor actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outcome)
            || !Desenlaces.Contains(outcome.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "outcome es requerido: approve | reject | request-info.", nameof(outcome));
        }

        return TransitarAsync(caseId, outcome.Trim(), note, actor, cancellationToken);
    }

    public Task<CaseDetail> TakeAsync(
        string caseId,
        CaseActor actor,
        string? note,
        CancellationToken cancellationToken = default)
        => TransitarAsync(caseId, "take", note ?? string.Empty, actor, cancellationToken);

    private async Task<CaseDetail> TransitarAsync(
        string caseId,
        string accion,
        string note,
        CaseActor actor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new ArgumentException("El expediente es obligatorio.", nameof(caseId));
        }
        ArgumentNullException.ThrowIfNull(actor);

        var rule = Transiciones[accion];
        var current = _recorder.Find(caseId)
            ?? throw new ArgumentException($"Expediente '{caseId.Trim()}' no encontrado.", nameof(caseId));

        // Idempotente: si el expediente YA está en el estado destino, devolverlo sin
        // re-auditar ni tocar el historial (núcleo anti-doble-efecto del dominio).
        if (current.Status == rule.To)
        {
            // Re-emite: el ledger del dispatcher deduplica por (caso, transición), así que es
            // inofensivo, y rescata el caso en que la primera acción no llegó a notificar
            // (notificaciones apagadas entonces, destinatario inválido, etc.).
            await _recorder.NotifyAsync(current, rule.To, _now(), cancellationToken);
            return current;
        }

        if (!rule.From.Contains(current.Status))
        {
            throw new InvalidOperationException(
                $"Acción ilegal: el expediente {current.Radicado} está en '{GovStatusSlugs.ToSlug(current.Status)}' y no admite '{accion}'.");
        }

        return await _recorder.RecordAsync(current, accion, rule.To, note, _now(), actor, cancellationToken);
    }
}
