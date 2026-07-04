using System;
using System.Linq;
using System.Threading.Tasks;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Xunit;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCaseWorkflowService"/> (seam <c>ICaseWorkflowService</c>,
/// máquina de estados AUDITADA del expediente — el diferenciador del dominio Gobierno):
/// empty (expediente vacío/desconocido) / happy (transición legal avanza + audita
/// append-only) / filter (transiciones ILEGALES rechazadas: saltos y terminales) /
/// idempotent (re-transicionar al estado actual no re-audita ni toca el timeline — el
/// caso central del dominio). Sobre los expedientes sembrados: case-1001 Radicado,
/// case-1002 EnRevision, case-1004 Resuelto, case-1005 Rechazado. ADR 0075.
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
    public async Task Transition_UnknownCase_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.TransitionAsync(string.Empty, CaseStatus.EnRevision, "nota"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.TransitionAsync("case-que-no-existe", CaseStatus.EnRevision, "nota"));
    }

    [Fact] // happy: transición legal avanza el estado + agrega timeline + audita
    public async Task Transition_Legal_AdvancesAndAudits()
    {
        var (workflow, cases, audit) = Make();

        var result = await workflow.TransitionAsync("case-1001", CaseStatus.EnRevision, "Asignado a revisión.");

        Assert.Equal(CaseStatus.EnRevision, result.Status);

        var detail = cases.FindCase("case-1001");
        Assert.Equal(CaseStatus.EnRevision, detail!.Status);
        Assert.Equal(2, detail.Timeline.Count);
        Assert.Equal("Asignado a revisión.", detail.Timeline[^1].Note);

        // CADA transición legal = evento append-only gov.case-transition (ADR 0037).
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.case-transition", evt.Action);
        Assert.Equal(detail.Radicado, evt.Resource);
        Assert.Contains("Radicado", evt.Detail);
        Assert.Contains("EnRevision", evt.Detail);
    }

    [Fact] // happy: la cadena completa del ciclo (con vuelta por subsanación) es legal
    public async Task Transition_FullLifecycleChain_IsLegal()
    {
        var (workflow, _, audit) = Make();

        // case-1002 está sembrado EnRevision.
        await workflow.TransitionAsync("case-1002", CaseStatus.Subsanacion, "Falta un documento.");
        await workflow.TransitionAsync("case-1002", CaseStatus.EnRevision, "Ciudadano subsanó.");
        var final = await workflow.TransitionAsync("case-1002", CaseStatus.Resuelto, "Licencia expedida.");

        Assert.Equal(CaseStatus.Resuelto, final.Status);
        Assert.Equal(3, audit.Events.Count); // una auditoría por transición legal
    }

    [Fact] // filter: saltarse etapas es ilegal (radicado no pasa directo a resuelto)
    public async Task Transition_SkippingStages_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.TransitionAsync("case-1001", CaseStatus.Resuelto, "salto ilegal"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.TransitionAsync("case-1001", CaseStatus.Subsanacion, "salto ilegal"));
    }

    [Fact] // filter: los estados terminales no admiten salida
    public async Task Transition_FromTerminalStates_Throws()
    {
        var (workflow, _, _) = Make();

        // case-1004 Resuelto y case-1005 Rechazado son terminales.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.TransitionAsync("case-1004", CaseStatus.EnRevision, "reabrir"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.TransitionAsync("case-1005", CaseStatus.EnRevision, "reabrir"));
    }

    [Fact] // filter: retroceder a radicado nunca es legal
    public async Task Transition_BackToRadicado_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.TransitionAsync("case-1002", CaseStatus.Radicado, "retroceso ilegal"));
    }

    [Fact] // idempotent: re-transicionar al estado actual no re-audita ni toca timeline
    public async Task Transition_ToCurrentState_IsIdempotent()
    {
        var (workflow, cases, audit) = Make();
        var before = cases.FindCase("case-1002")!.Timeline.Count;

        var result = await workflow.TransitionAsync("case-1002", CaseStatus.EnRevision, "repetida");

        Assert.Equal(CaseStatus.EnRevision, result.Status);
        Assert.Empty(audit.Events); // sin doble efecto
        Assert.Equal(before, cases.FindCase("case-1002")!.Timeline.Count);
    }

    [Fact] // el expediente también transiciona referenciado por número de radicado
    public async Task Transition_ByRadicado_Works()
    {
        var (workflow, cases, _) = Make();

        var result = await workflow.TransitionAsync("RAD-2026-1001", CaseStatus.EnRevision, "por radicado");

        Assert.Equal(CaseStatus.EnRevision, result.Status);
        Assert.Equal(CaseStatus.EnRevision, cases.FindCase("case-1001")!.Status);
    }
}
