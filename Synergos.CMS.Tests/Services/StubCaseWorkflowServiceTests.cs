using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Cubre <see cref="StubCaseWorkflowService"/> (seam <c>ICaseWorkflowService</c>,
/// máquina de estados AUDITADA del expediente — el diferenciador del dominio Gobierno):
/// empty (expediente vacío/desconocido / outcome inválido) / happy (decisión legal
/// avanza + audita append-only + registra la resolución) / filter (decisiones ILEGALES
/// rechazadas: sobre terminales) / idempotent (aplicar el outcome cuyo estado destino ya
/// es el actual no re-audita — el caso central del dominio). Sobre los expedientes
/// sembrados: case-1001 submitted, case-1002 in-review, case-1003 info-requested, case-1004
/// approved, case-1005 rejected. ADR 0075.
/// </summary>
/// <remarks>
/// <para><b>Es la v2 del proceso</b> (009-gobierno-tramite): se TOMA antes de decidir (P4) y lo
/// que firma cada acción es la persona de la sesión, no un actor de demo (D-5).</para>
/// </remarks>
public class StubCaseWorkflowServiceTests
{
    private static readonly DateTimeOffset Clock = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly CaseActor Ana = new(
        CaseRoles.KindFuncionario, "f-ana", "Ana Funcionaria", "ana@entidad.gov.co");

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
            workflow.DecideAsync(string.Empty, "approve", "nota", Ana));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.DecideAsync("case-que-no-existe", "approve", "nota", Ana));
    }

    [Fact] // empty: outcome desconocido lanza ArgumentException
    public async Task Decide_UnknownOutcome_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.DecideAsync("case-1002", "maybe", "nota", Ana));
    }

    [Fact] // empty: tomar NO es una decisión — no entra por DecideAsync
    public async Task Decide_Take_IsNotAnOutcome()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            workflow.DecideAsync("case-1001", "take", "nota", Ana));
    }

    [Fact] // happy: decisión legal avanza el estado + agrega timeline + audita + resuelve
    public async Task Decide_Approve_AdvancesAndAudits()
    {
        var (workflow, cases, audit) = Make();

        var updated = await workflow.DecideAsync("case-1002", "approve", "Licencia expedida.", Ana);

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

        var updated = await workflow.DecideAsync("case-1002", "request-info", "Falta un documento.", Ana);

        Assert.Equal(CaseStatus.Subsanacion, updated.Status);
        Assert.Null(updated.Decision); // request-info no es una resolución terminal
        Assert.Single(audit.Events);
    }

    /// <summary>
    /// En subsanación el funcionario ya no decide: espera a que el ciudadano responda.
    /// </summary>
    /// <remarks>
    /// En la v1 se podía aprobar un expediente al que se le acababa de pedir información, sin
    /// que la información llegara. En la v2 de subsanación sólo se sale por la respuesta del
    /// ciudadano, que lo devuelve a revisión (P8).
    /// </remarks>
    [Fact]
    public async Task Decide_InfoThenApprove_IsIllegal_UntilTheCitizenAnswers()
    {
        var (workflow, cases, audit) = Make();

        await workflow.DecideAsync("case-1002", "request-info", "Falta un documento.", Ana);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DecideAsync("case-1002", "approve", "Licencia expedida sin subsanar.", Ana));

        Assert.Equal(CaseStatus.Subsanacion, cases.FindCase("case-1002")!.Status);
        Assert.Single(audit.Events); // la ilegal no se asienta
    }

    /// <summary>
    /// Tomar antes de decidir (P4): un expediente recién radicado no se decide.
    /// </summary>
    /// <remarks>
    /// <b>Es D-1 de 009.</b> Con la v1 nada llevaba un expediente a «en revisión»: se decidía
    /// directo desde radicado y la cola no podía distinguir lo que alguien ya estaba mirando.
    /// </remarks>
    [Theory]
    [InlineData("approve")]
    [InlineData("reject")]
    [InlineData("request-info")]
    public async Task Decide_FromSubmitted_IsIllegal_TakeFirst(string outcome)
    {
        var (workflow, cases, audit) = Make();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DecideAsync("case-1001", outcome, "sin tomar", Ana));

        Assert.Contains("'submitted'", ex.Message, StringComparison.Ordinal);
        Assert.Equal(CaseStatus.Radicado, cases.FindCase("case-1001")!.Status);
        Assert.Empty(audit.Events);
    }

    [Fact] // happy: tomar lleva de radicado a revisión y se asienta como paso, no como decisión
    public async Task Take_MovesSubmittedToInReview_AndAuditsAsTransition()
    {
        var (workflow, cases, audit) = Make();

        var updated = await workflow.TakeAsync("case-1001", Ana, null);

        Assert.Equal(CaseStatus.EnRevision, updated.Status);
        Assert.Null(updated.Decision);
        Assert.Equal(CaseStatus.EnRevision, cases.FindCase("case-1001")!.Status);

        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.case-transition", evt.Action);
        Assert.Contains("submitted → in-review (take)", evt.Detail);
        Assert.Contains("Expediente tomado para revisión.", evt.Detail);
    }

    [Fact] // happy: tomado, ya se decide
    public async Task Take_ThenApprove_IsLegal()
    {
        var (workflow, _, audit) = Make();

        await workflow.TakeAsync("case-1001", Ana, "La reviso yo.");
        var final = await workflow.DecideAsync("case-1001", "approve", "Certificado expedido.", Ana);

        Assert.Equal(CaseStatus.Resuelto, final.Status);
        Assert.Equal(2, audit.Events.Count); // una auditoría por acción legal
    }

    [Fact] // idempotent: tomar lo ya tomado no re-audita
    public async Task Take_Twice_IsIdempotent()
    {
        var (workflow, _, audit) = Make();

        await workflow.TakeAsync("case-1001", Ana, null);
        var segunda = await workflow.TakeAsync("case-1001", Ana, null);

        Assert.Equal(CaseStatus.EnRevision, segunda.Status);
        Assert.Single(audit.Events);
    }

    [Fact] // filter: lo resuelto no se vuelve a tomar
    public async Task Take_FromTerminalStates_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.TakeAsync("case-1004", Ana, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.TakeAsync("case-1005", Ana, null));
    }

    [Fact] // filter: en subsanación tampoco se toma — se espera al ciudadano
    public async Task Take_FromInfoRequested_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.TakeAsync("case-1003", Ana, null));
    }

    [Fact] // filter: los estados terminales no admiten nuevas decisiones
    public async Task Decide_FromTerminalStates_Throws()
    {
        var (workflow, _, _) = Make();

        // case-1004 approved y case-1005 rejected son terminales.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DecideAsync("case-1004", "reject", "reabrir", Ana));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            workflow.DecideAsync("case-1005", "approve", "reabrir", Ana));
    }

    [Fact] // idempotent: aprobar un expediente ya aprobado no re-audita ni cambia nada
    public async Task Decide_ToCurrentTerminalState_IsIdempotent()
    {
        var (workflow, _, audit) = Make();

        // case-1004 ya está approved (Resuelto) → approve es idempotente.
        var result = await workflow.DecideAsync("case-1004", "approve", "repetida", Ana);

        Assert.Equal(CaseStatus.Resuelto, result.Status);
        Assert.Empty(audit.Events); // sin doble efecto
    }

    [Fact] // el expediente también decide referenciado por número de radicado
    public async Task Decide_ByRadicado_Works()
    {
        var (workflow, cases, _) = Make();

        var result = await workflow.DecideAsync("SG-2026-001002", "request-info", "por radicado", Ana);

        Assert.Equal(CaseStatus.Subsanacion, result.Status);
        Assert.Equal(CaseStatus.Subsanacion, cases.FindCase("case-1002")!.Status);
    }

    /// <summary>
    /// Quien firma la decisión es la persona de la sesión, no el actor de demo (D-5 de 009).
    /// </summary>
    /// <remarks>
    /// Antes toda decisión salía firmada por <see cref="GovCaseDecisionRecorder.OfficerActor"/>: el
    /// control interno no podía saber quién decidió, y dos funcionarias eran la misma persona.
    /// </remarks>
    [Fact]
    public async Task Decide_IsSignedByTheSessionPerson_NotTheDemoActor()
    {
        var (workflow, _, audit) = Make();

        var updated = await workflow.DecideAsync("case-1002", "reject", "No cumple.", Ana);

        Assert.Equal("ana@entidad.gov.co", updated.Decision!.DecidedBy);
        var evt = Assert.Single(audit.Events);
        Assert.Equal("ana@entidad.gov.co", evt.ActorEmail);
        Assert.Equal("Ana Funcionaria", evt.ActorName);
        Assert.DoesNotContain(audit.Events, e => e.ActorEmail == GovCaseDecisionRecorder.OfficerActor);
    }

    [Fact] // otra persona, otra firma: la firma sale del actor, no de una constante
    public async Task Take_IsSignedByWhoeverTookIt()
    {
        var (workflow, _, audit) = Make();
        var beto = new CaseActor(CaseRoles.KindFuncionario, "f-beto", "Beto", "beto@entidad.gov.co");

        await workflow.TakeAsync("case-1001", beto, null);

        Assert.Equal("beto@entidad.gov.co", Assert.Single(audit.Events).ActorEmail);
    }

    // ── Las acciones servidas (T008): lo que se ofrece es lo que este motor aceptaría ──

    [Fact] // cada expediente sembrado ofrece a cada cara lo de su estado
    public async Task Actions_FollowTheStateAndTheFace()
    {
        var (workflow, _, _) = Make();

        Assert.Equal(new[] { "take" }, await workflow.ActionsAsync("case-1001", CaseRoles.Funcionario));
        Assert.Equal(new[] { "approve", "reject", "request-info" }, await workflow.ActionsAsync("case-1002", CaseRoles.Funcionario));
        Assert.Empty(await workflow.ActionsAsync("case-1003", CaseRoles.Funcionario));
        Assert.Equal(new[] { "resubmit" }, await workflow.ActionsAsync("case-1003", CaseRoles.Ciudadano));
        Assert.Empty(await workflow.ActionsAsync("case-1001", CaseRoles.Ciudadano));
        Assert.Empty(await workflow.ActionsAsync("case-1004", CaseRoles.Funcionario));
        Assert.Empty(await workflow.ActionsAsync("case-1005", CaseRoles.Ciudadano));
    }

    [Fact] // las acciones se mueven con el expediente (y se piden también por radicado)
    public async Task Actions_MoveWithTheCase()
    {
        var (workflow, _, _) = Make();

        await workflow.TakeAsync("case-1001", Ana, null);

        Assert.Equal(new[] { "approve", "reject", "request-info" },
            await workflow.ActionsAsync("SG-2026-001001", CaseRoles.Funcionario));
    }

    [Fact] // empty: el expediente que no existe es error de quien pregunta
    public async Task Actions_UnknownCase_Throws()
    {
        var (workflow, _, _) = Make();

        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ActionsAsync("case-que-no-existe", CaseRoles.Funcionario));
        await Assert.ThrowsAsync<ArgumentException>(() => workflow.ActionsAsync(" ", CaseRoles.Funcionario));
    }

    /// <summary>
    /// Lo ofrecido sale bien: cada acción servida al funcionario se acepta y mueve el expediente (SC-002).
    /// </summary>
    /// <remarks>
    /// Es la propiedad que la lista fija del elemento no tenía: con ella, tres ofertas acababan en un
    /// rechazo y una en nada. Cada acción se prueba sobre un motor recién sembrado.
    /// </remarks>
    [Theory]
    [InlineData("case-1001")]
    [InlineData("case-1002")]
    public async Task Every_offered_officer_action_succeeds_and_moves_the_case(string caso)
    {
        var (sembrado, _, _) = Make();
        var ofrecidas = await sembrado.ActionsAsync(caso, CaseRoles.Funcionario);
        Assert.NotEmpty(ofrecidas);

        foreach (var accion in ofrecidas)
        {
            var (workflow, cases, _) = Make();
            var antes = cases.FindCase(caso)!.Status;

            var despues = accion == "take"
                ? await workflow.TakeAsync(caso, Ana, null)
                : await workflow.DecideAsync(caso, accion, "nota", Ana);

            Assert.NotEqual(antes, despues.Status);
        }
    }

    // ── Subsanar (T012): el ciudadano devuelve el expediente a revisión (D-2) ──

    private static readonly CaseActor Juliana = new(
        CaseRoles.KindCiudadano, "c-juliana", "Juliana Ríos", "juliana.rios@correo.co");

    [Fact] // happy: de subsanación a revisión, a nombre del ciudadano, asentado como paso y no como decisión
    public async Task Resubmit_MovesInfoRequestedBackToReview_SignedByTheCitizen()
    {
        var (workflow, cases, audit) = Make();

        var updated = await workflow.ResubmitAsync("case-1003", Juliana, "Adjunté el certificado.");

        Assert.Equal(CaseStatus.EnRevision, updated.Status);
        Assert.Null(updated.Decision);
        Assert.Equal(CaseStatus.EnRevision, cases.FindCase("case-1003")!.Status);
        var evt = Assert.Single(audit.Events);
        Assert.Equal("gov.case-transition", evt.Action);
        Assert.Equal("juliana.rios@correo.co", evt.ActorEmail);
        Assert.Contains("info-requested → in-review (resubmit): Adjunté el certificado.", evt.Detail);
    }

    [Theory] // filter: sólo se subsana lo que está en subsanación
    [InlineData("case-1001")] // radicado
    [InlineData("case-1004")] // aprobado
    [InlineData("case-1005")] // rechazado
    public async Task Resubmit_FromAnyOtherState_Throws(string caso)
    {
        var (workflow, cases, audit) = Make();
        var antes = cases.FindCase(caso)!.Status;

        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ResubmitAsync(caso, Juliana, null));

        Assert.Equal(antes, cases.FindCase(caso)!.Status);
        Assert.Empty(audit.Events);
    }

    [Fact] // idempotent: subsanar dos veces da el expediente como quedó (el doble clic del ciudadano)
    public async Task Resubmit_Twice_IsIdempotent()
    {
        var (workflow, _, audit) = Make();

        await workflow.ResubmitAsync("case-1003", Juliana, null);
        var segunda = await workflow.ResubmitAsync("case-1003", Juliana, null);

        Assert.Equal(CaseStatus.EnRevision, segunda.Status);
        Assert.Single(audit.Events);
    }

    [Fact] // vuelto a revisión, el funcionario vuelve a decidir y al ciudadano no le queda nada que hacer
    public async Task Resubmit_GivesTheDecisionsBackToTheOfficer()
    {
        var (workflow, _, _) = Make();

        await workflow.ResubmitAsync("case-1003", Juliana, null);

        Assert.Equal(new[] { "approve", "reject", "request-info" },
            await workflow.ActionsAsync("case-1003", CaseRoles.Funcionario));
        Assert.Empty(await workflow.ActionsAsync("case-1003", CaseRoles.Ciudadano));
    }

    [Fact] // lo ofrecido sale bien también para el ciudadano: subsanar lo que se le pidió
    public async Task The_offered_citizen_action_succeeds_and_moves_the_case()
    {
        var (workflow, cases, _) = Make();

        Assert.Equal(new[] { "resubmit" }, await workflow.ActionsAsync("case-1003", CaseRoles.Ciudadano));
        var despues = await workflow.ResubmitAsync("case-1003", Juliana, null);

        Assert.NotEqual(CaseStatus.Subsanacion, despues.Status);
    }

    private sealed class AvisosRecibidos : ITransactionalNotifier
    {
        public List<string?> Claves { get; } = new();

        public Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            Claves.Add(notification.DedupeKey);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Pedir información dos veces avisa dos veces: la clave del aviso lleva la vuelta.
    /// </summary>
    /// <remarks>
    /// Con la subsanación que vuelve a revisión, la segunda petición tenía la misma clave que la primera
    /// y el ledger la daba por vista: el ciudadano no se enteraba de que le volvían a pedir algo. La
    /// primera entrada conserva la clave de siempre (lo ya avisado no se repite) y el doble clic no
    /// cambia la cuenta (el ledger lo sigue descartando).
    /// </remarks>
    [Fact]
    public async Task A_second_request_info_cycle_notifies_the_citizen_again()
    {
        var avisos = new AvisosRecibidos();
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider(), null, () => Clock);
        var workflow = new StubCaseWorkflowService(cases, null, () => Clock, avisos);
        var carlos = new CaseActor(CaseRoles.KindCiudadano, "c-carlos", "Carlos Andrés Pérez", "carlos.perez@correo.co");
        var raiz = $"{NotificationTypes.GovCaseDecided}:SG-2026-001002";

        await workflow.DecideAsync("case-1002", "request-info", "Falta la foto.", Ana);
        await workflow.ResubmitAsync("case-1002", carlos, null);
        await workflow.DecideAsync("case-1002", "request-info", "La foto no se ve.", Ana);
        await workflow.DecideAsync("case-1002", "request-info", "La foto no se ve.", Ana);

        Assert.Equal(new[]
        {
            $"{raiz}:info-requested",
            $"{raiz}:in-review:2",
            $"{raiz}:info-requested:2",
            $"{raiz}:info-requested:2",
        }, avisos.Claves);
    }

    [Fact] // la firma cae al nombre, luego a la identidad y sólo sin ninguna al respaldo declarado
    public void Firma_FallsBackToNameThenIdentityThenTheDeclaredFallback()
    {
        Assert.Equal("ana@entidad.gov.co", GovCaseDecisionRecorder.Firma(Ana));
        Assert.Equal("Ana Funcionaria", GovCaseDecisionRecorder.Firma(Ana with { Email = " " }));
        Assert.Equal("gov.funcionario:f-ana", GovCaseDecisionRecorder.Firma(Ana with { Email = "", DisplayName = "" }));
        Assert.Equal(GovCaseDecisionRecorder.OfficerActor,
            GovCaseDecisionRecorder.Firma(new CaseActor(CaseRoles.KindFuncionario, " ", "", "")));
    }
}
