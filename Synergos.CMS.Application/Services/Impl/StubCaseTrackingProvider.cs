using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// Default <see cref="ICaseTrackingProvider"/> — seguimiento STUB de expedientes del
/// vertical Gobierno (doc gobierno-app-spec §4). Expone el ciclo de vida del expediente
/// a las dos bandejas: <see cref="GetCaseAsync"/> → expediente + estado + timeline
/// (seguimiento del ciudadano / detalle del funcionario); <see cref="GetInboxAsync"/> →
/// la cola filtrada por actor + rol (el ciudadano ve SOLO los suyos por email; el
/// funcionario/admin ve toda la cola). El tracking es el diferenciador del dominio
/// (research GOV.CO: fecha de radicación, etapa, estado).
/// </summary>
/// <remarks>
/// Lógica pura en <c>Synergos.CMS.Application</c> — cero dependencia de
/// Umbraco/AspNetCore (ADR 0002). NO duplica estado: LEE el agregado de
/// <see cref="StubApplicationService"/> por composición (DIP, mismo patrón
/// <c>StubEventManagementService</c> → <c>StubEventTicketingService</c>). El adapter
/// real (DB / sistema de la entidad) implementa la misma seam. ADR 0075 (filter es el
/// caso central: ciudadano vs cola completa).
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

    public Task<IReadOnlyList<CaseInboxItem>> GetInboxAsync(string actor, string role, CancellationToken cancellationToken = default)
    {
        var isCitizen = string.IsNullOrWhiteSpace(role)
            || string.Equals(role.Trim(), "citizen", StringComparison.OrdinalIgnoreCase);

        var all = _cases.ListCases();

        var filtered = isCitizen
            ? all.Where(c => !string.IsNullOrWhiteSpace(actor)
                && string.Equals(c.Citizen.Email, actor.Trim(), StringComparison.OrdinalIgnoreCase))
            : all;

        IReadOnlyList<CaseInboxItem> inbox = filtered
            .OrderByDescending(c => c.RadicadoAt)
            .Select(c => new CaseInboxItem(
                CaseId: c.CaseId,
                Radicado: c.Radicado,
                TramiteName: c.TramiteName,
                CitizenName: c.Citizen.Name,
                Status: c.Status,
                RadicadoAt: c.RadicadoAt))
            .ToList();

        return Task.FromResult(inbox);
    }
}
