using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICaseTrackingProvider"/> — seguimiento STUB de expedientes del
/// vertical Gobierno (doc gobierno.md §4). Expone el ciclo de vida del expediente a las
/// dos bandejas: <see cref="GetCaseAsync"/> → expediente + estado + timeline (seguimiento
/// del ciudadano / detalle del funcionario); <see cref="ListForCitizenAsync"/> → los
/// expedientes del ciudadano por email; <see cref="GetQueueAsync"/> → la cola del
/// funcionario filtrada por entidad + estado, ordenada por prioridad y SLA. El tracking
/// es el diferenciador del dominio (research GOV.CO: fecha de radicación, etapa, días
/// restantes).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica estado: LEE el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP, mismo patrón
/// <c>StubEventManagementService</c> → <c>StubEventTicketingService</c>). El adapter
/// real (DB / sistema de la entidad) implementa la misma seam. ADR 0075 (filter es el
/// caso central: ciudadano vs cola por entidad/estado).
/// </remarks>
public sealed class StubCaseTrackingProvider : ICaseTrackingProvider
{
    private readonly StubApplicationService _cases;

    public StubCaseTrackingProvider(StubApplicationService cases)
    {
        _cases = cases ?? throw new ArgumentNullException(nameof(cases));
    }

    public Task<CaseDetail?> GetCaseAsync(string caseId, CancellationToken cancellationToken = default)
        => Task.FromResult(_cases.FindCase(caseId));

    public Task<IReadOnlyList<CaseInboxItem>> ListForMemberAsync(Guid memberKey, CancellationToken cancellationToken = default)
    {
        // Por MemberKey, no por email: el correo lo teclea quien radica y es enumerable.
        // Los expedientes de invitado (MemberKey null) no entran — no tienen dueño.
        IReadOnlyList<CaseInboxItem> inbox = _cases.ListCasesWithAgency()
            .Where(x => x.Case.Citizen.MemberKey == memberKey)
            .OrderByDescending(x => x.Case.RadicadoAt)
            .Select(x => ToInboxItem(x.Case))
            .ToList();

        return Task.FromResult(inbox);
    }

    public Task<IReadOnlyList<CaseInboxItem>> GetQueueAsync(string? agency, string? status, CancellationToken cancellationToken = default)
    {
        var byAgency = string.IsNullOrWhiteSpace(agency);
        var statusFilter = GovStatusSlugs.TryParse(status, out var parsed)
            ? parsed
            : (CaseStatus?)null;

        IReadOnlyList<CaseInboxItem> queue = _cases.ListCasesWithAgency()
            .Where(x => byAgency
                || string.Equals(x.Agency, agency!.Trim(), StringComparison.OrdinalIgnoreCase))
            .Where(x => statusFilter is null || x.Case.Status == statusFilter)
            .OrderByDescending(x => x.Case.Priority) // High → Normal → Low
            .ThenBy(x => x.Case.SlaDaysLeft)          // menos días restantes primero
            .Select(x => ToInboxItem(x.Case))
            .ToList();

        return Task.FromResult(queue);
    }

    private static CaseInboxItem ToInboxItem(CaseDetail c) => new(
        CaseId: c.CaseId,
        Radicado: c.Radicado,
        TramiteName: c.TramiteName,
        CitizenName: c.Citizen.Name,
        Status: c.Status,
        CurrentStage: c.CurrentStage,
        Priority: c.Priority,
        SlaDaysLeft: c.SlaDaysLeft,
        RadicadoAt: c.RadicadoAt);
}
