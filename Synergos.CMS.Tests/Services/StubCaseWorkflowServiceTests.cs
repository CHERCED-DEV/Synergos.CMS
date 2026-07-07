using System;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCaseWorkflowService"/> (seam <c>ICaseWorkflowService</c>,
/// máquina de estados AUDITADA del expediente — el diferenciador del dominio Gobierno):
/// empty (expediente vacío/desconocido / outcome inválido) / happy (decisión legal
/// avanza + audita append-only + registra la resolución) / filter (decisiones ILEGALES
/// rechazadas: sobre terminales) / idempotent (aplicar el outcome cuyo estado destino ya
/// es el actual no re-audita — el caso central del dominio). Sobre los expedientes
/// sembrados: case-1001 submitted, case-1002 in-review, case-1004 approved, case-1005
/// rejected. ADR 0075.
/// </summary>
public class StubCaseWorkflowServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static (StubCaseWorkflowService Workflow, StubApplicationService Cases, RecordingAuditTrailWriter Audit) Make()
    {
        var audit = new RecordingAuditTrailWriter();
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(),
            new StubGovFeeCalculator(),
            new StubPaymentProvider(),
            null,
            () => Clock);
        var workflow = new StubCaseWorkflowService(cases, audit, () => Clock);
        return (workflow, cases, audit);
    }

    [Fact] // empty: expediente vacío o desconocido lanza ArgumentException
    public async Task Decide_UnknownCase_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.DecideAsync(string.Empty, "approve", "nota"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.DecideAsync("case-que-no-existe", "approve", "nota"));
    }

    [Fact] // empty: outcome desconocido lanza ArgumentException
    public async Task Decide_UnknownOutcome_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.DecideAsync("case-1002", "maybe", "nota"));
    }

    [Fact] // happy: decisión legal avanza el estado + agrega timeline + audita + resuelve
    public async Task Decide_Approve_AdvancesAndAudits()
    {
        var (workflow, cases, audit) = Make();

        var updated = await workflow.DecideAsync("case-1002", "approve", "Licencia expedida.");

        Assert.Equal(CaseStatus.Resuelto, updated.Status);
        Assert.Equal("Aprobado", updated.CurrentStage);
        Assert.NotNull(updated.Decision);
        Assert.Equal("approve", updated.Decision!.Outcome);
        Assert.Equal("Licencia expedida.", updated.Decision.Note);

        Assert.Equal(CaseStatus.Resuelto, cases.FindCase("case-1002")!.Status);

        // CADA decisión legal = evento append-only gov.case-decision (ADR 0037).
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.case-decision", evt.Action);
        Assert.Equal(updated.Radicado, evt.Resource);
        Assert.Contains("approve", evt.Detail);
    }

    [Fact] // happy: request-info lleva a subsanación y NO registra resolución terminal
    public async Task Decide_RequestInfo_MovesToInfoRequested()
    {
        var (workflow, _, audit) = Make();

        var updated = await workflow.DecideAsync("case-1002", "request-info", "Falta un documento.");

        Assert.Equal(CaseStatus.Subsanacion, updated.Status);
        Assert.Null(updated.Decision); // request-info no es una resolución terminal
        Assert.Single(audit.Events);
    }

    [Fact] // happy: el ciclo request-info → approve es legal (subsanación → aprobado)
    public async Task Decide_InfoThenApprove_IsLegal()
    {
        var (workflow, _, audit) = Make();

        await workflow.DecideAsync("case-1002", "request-info", "Falta un documento.");
        var final = await workflow.DecideAsync("case-1002", "approve", "Licencia expedida tras subsanar.");

        Assert.Equal(CaseStatus.Resuelto, final.Status);
        Assert.Equal(2, audit.Events.Count); // una auditoría por decisión legal
    }

    [Fact] // filter: los estados terminales no admiten nuevas decisiones
    public async Task Decide_FromTerminalStates_Throws()
    {
        var (workflow, _, _) = Make();

        // case-1004 approved y case-1005 rejected son terminales.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DecideAsync("case-1004", "reject", "reabrir"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DecideAsync("case-1005", "approve", "reabrir"));
    }

    [Fact] // idempotent: aprobar un expediente ya aprobado no re-audita ni cambia nada
    public async Task Decide_ToCurrentTerminalState_IsIdempotent()
    {
        var (workflow, _, audit) = Make();

        // case-1004 ya está approved (Resuelto) → approve es idempotente.
        var result = await workflow.DecideAsync("case-1004", "approve", "repetida");

        Assert.Equal(CaseStatus.Resuelto, result.Status);
        Assert.Empty(audit.Events); // sin doble efecto
    }

    [Fact] // el expediente también decide referenciado por número de radicado
    public async Task Decide_ByRadicado_Works()
    {
        var (workflow, cases, _) = Make();

        var result = await workflow.DecideAsync("SG-2026-001001", "request-info", "por radicado");

        Assert.Equal(CaseStatus.Subsanacion, result.Status);
        Assert.Equal(CaseStatus.Subsanacion, cases.FindCase("case-1001")!.Status);
    }
}
