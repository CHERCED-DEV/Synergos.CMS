using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// Tests del RBAC de la CONSOLA DEL FUNCIONARIO en <see cref="GovController"/> (T2 Gobierno,
/// segunda mitad). Las 3 rutas del funcionario —cola, expediente, decisión— exponen PII de
/// otros ciudadanos (cédula/correo/teléfono en las respuestas del formulario) y permiten
/// decidir expedientes. Antes eran ANÓNIMAS: cualquiera enumeraba la cola y decidía casos.
/// El guard <c>RequireOfficer()</c> las cierra con el molde de dos capas de
/// <c>DashboardApiController</c>: anónimo → 401, autenticado sin rol → 403, funcionario → pasa.
/// La identidad del ciudadano (las 3 rutas del ciudadano) se cubre aparte; aquí es SOLO el rol.
/// ADR 0075.
/// </summary>
public sealed class GovControllerTests
{
    private readonly ITramiteCatalogProvider _catalog = Substitute.For<ITramiteCatalogProvider>();
    private readonly IApplicationService _applications = Substitute.For<IApplicationService>();
    private readonly ICaseWorkflowService _workflow = Substitute.For<ICaseWorkflowService>();
    private readonly ICaseTrackingProvider _tracking = Substitute.For<ICaseTrackingProvider>();
    private readonly IDocumentUploadService _documents = Substitute.For<IDocumentUploadService>();
    private readonly IPrivateFileStore _files = Substitute.For<IPrivateFileStore>();
    private readonly IMessagingService _messaging = Substitute.For<IMessagingService>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IGovActNotificationService _notifications = Substitute.For<IGovActNotificationService>();
    private readonly IAuditTrailWriter _audit = Substitute.For<IAuditTrailWriter>();

    /// <summary>
    /// El controller lleva un <see cref="Microsoft.AspNetCore.Http.DefaultHttpContext"/>
    /// porque la descarga escribe cabeceras de seguridad en <c>Response</c> (nosniff);
    /// sin contexto eso revienta con NullReference y el test no vería el código real.
    /// </summary>
    private GovController BuildSut() => new(
        _catalog, _applications, _workflow, _tracking, _documents, _files, _messaging, _gate, _notifications, _audit)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
        },
    };

    private void Anonymous() => _gate.IsAuthenticated.Returns(false);

    private void AuthenticatedWithoutOfficerRole()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

    private void Officer()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private static readonly GovController.DecisionRequest ValidDecision = new("case-1001", "approve", "ok");

    // ── empty (anónimo): las 3 rutas del funcionario niegan 401 y NO tocan el seam ──────

    [Fact]
    public async Task Queue_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().Queue(null, null, default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _tracking.DidNotReceive().GetQueueAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Case_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().Case("case-1001", default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _tracking.DidNotReceive().GetCaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decision_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().Decision(ValidDecision, default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _workflow.DidNotReceive().DecideAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<CancellationToken>());
    }

    // ── filter (autenticado SIN rol funcionario): 403, no basta con estar logueado ─────

    [Fact]
    public async Task Queue_AuthenticatedNonOfficer_Returns403_AndSkipsSeam()
    {
        AuthenticatedWithoutOfficerRole();

        var result = await BuildSut().Queue(null, null, default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        await _tracking.DidNotReceive().GetQueueAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Case_AuthenticatedNonOfficer_Returns403_AndSkipsSeam()
    {
        AuthenticatedWithoutOfficerRole();

        var result = await BuildSut().Case("case-1001", default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        await _tracking.DidNotReceive().GetCaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Decision_AuthenticatedNonOfficer_Returns403_AndSkipsSeam()
    {
        AuthenticatedWithoutOfficerRole();

        var result = await BuildSut().Decision(ValidDecision, default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        await _workflow.DidNotReceive().DecideAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<CancellationToken>());
    }

    // ── happy (funcionario): el guard deja pasar y delega en el seam ───────────────────

    [Fact]
    public async Task Queue_Officer_PassesGuard_AndCallsSeam()
    {
        Officer();
        _tracking.GetQueueAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<CaseInboxItem>());

        var result = await BuildSut().Queue(null, null, default);

        Assert.IsType<OkObjectResult>(result);
        await _tracking.Received(1).GetQueueAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ── D-5 de 009: quien firma la decisión es la persona de la sesión ─────────────────

    [Fact] // happy: el actor sale del GATE —clave, nombre y correo de la cuenta—, nunca del cuerpo
    public async Task Decision_Officer_PassesTheSessionPersonAsActor()
    {
        Officer();
        var clave = System.Guid.NewGuid();
        _gate.CurrentMemberKey.Returns(clave);
        _gate.CurrentMemberEmail.Returns("ana@entidad.gov.co");
        _gate.CurrentMemberDisplayName.Returns("Ana Funcionaria");
        _workflow.DecideAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<CancellationToken>())
            .Returns(CaseWithDocument("file-1"));

        var result = await BuildSut().Decision(ValidDecision, default);

        Assert.IsType<OkObjectResult>(result);
        await _workflow.Received(1).DecideAsync(
            "case-1001", "approve", "ok",
            Arg.Is<CaseActor>(a => a.Kind == CaseRoles.KindFuncionario
                && a.Id == clave.ToString("N")
                && a.Email == "ana@entidad.gov.co"
                && a.DisplayName == "Ana Funcionaria"),
            Arg.Any<CancellationToken>());
    }

    // ── control: el rol que se exige es el de funcionario, no uno cualquiera ────────────

    [Fact]
    public async Task Queue_Officer_ChecksTheOfficerRoleCsv()
    {
        Officer();
        _tracking.GetQueueAsync(Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(System.Array.Empty<CaseInboxItem>());

        await BuildSut().Queue(null, null, default);

        // El CSV consultado debe nombrar 'funcionario' (rol de dominio), no venir vacío
        // (que sería "cualquier logueado") ni pedir solo 'admin'.
        _gate.Received().HasAnyRole(Arg.Is<string?>(csv => csv != null && csv.Contains("funcionario")));
    }

    // ══════════ 009 — TOMAR EL EXPEDIENTE Y LAS ACCIONES SERVIDAS (T009) ══════════
    //
    // Decidir exige tomar antes (P4), y la consola y el portal pintan sólo lo que el proceso
    // permite desde el estado a la cara que mira (D-3): ningún botón que acabe en un rechazo.

    private static readonly System.Guid FuncionariaKey = System.Guid.NewGuid();

    private void OfficerAna()
    {
        Officer();
        _gate.CurrentMemberKey.Returns(FuncionariaKey);
        _gate.CurrentMemberEmail.Returns("ana@entidad.gov.co");
        _gate.CurrentMemberDisplayName.Returns("Ana Funcionaria");
    }

    /// <summary>El controlador con el motor en proceso de verdad, para lo que depende del estado.</summary>
    private (GovController Sut, StubApplicationService Cases) BuildWithStubEngine()
    {
        var cases = new StubApplicationService(
            new StubTramiteCatalogProvider(), new StubGovFeeCalculator(), new StubPaymentProvider());
        var sut = new GovController(
            _catalog, _applications, new StubCaseWorkflowService(cases), new StubCaseTrackingProvider(cases),
            _documents, _files, _messaging, _gate, _notifications, _audit)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext(),
            },
        };
        return (sut, cases);
    }

    private static GovController.CaseDto CaseOf(IActionResult result)
        => Assert.IsType<GovController.CaseResponse>(Assert.IsType<OkObjectResult>(result).Value).Case;

    [Fact] // empty (anónimo): 401 y no toca el motor
    public async Task Take_Anonymous_Returns401_AndSkipsSeam()
    {
        Anonymous();

        var result = await BuildSut().Take("case-1001", null, default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _workflow.DidNotReceive().TakeAsync(
            Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // filter: un ciudadano autenticado no toma expedientes — 403
    public async Task Take_AuthenticatedNonOfficer_Returns403_AndSkipsSeam()
    {
        AuthenticatedWithoutOfficerRole();

        var result = await BuildSut().Take("case-1001", null, default);

        Assert.Equal(403, Assert.IsType<ObjectResult>(result).StatusCode);
        await _workflow.DidNotReceive().TakeAsync(
            Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // empty: el expediente que no existe es 404, sin llegar al motor
    public async Task Take_UnknownCase_Returns404_AndSkipsSeam()
    {
        OfficerAna();
        _tracking.GetCaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((CaseDetail?)null);

        var result = await BuildSut().Take("case-que-no-existe", null, default);

        Assert.IsType<NotFoundObjectResult>(result);
        await _workflow.DidNotReceive().TakeAsync(
            Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact] // happy: toma a nombre de la persona de la sesión, con su nota
    public async Task Take_Officer_TakesAsTheSessionPerson()
    {
        OfficerAna();
        CaseExists();
        _workflow.TakeAsync(Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(CaseWithDocument("file-1"));

        var result = await BuildSut().Take("case-1001", new GovController.TakeRequest("La reviso yo."), default);

        Assert.IsType<OkObjectResult>(result);
        await _workflow.Received(1).TakeAsync(
            "case-1001",
            Arg.Is<CaseActor>(a => a.Kind == CaseRoles.KindFuncionario
                && a.Id == FuncionariaKey.ToString("N")
                && a.Email == "ana@entidad.gov.co"),
            "La reviso yo.",
            Arg.Any<CancellationToken>());
    }

    [Fact] // filter: lo ilegal, y «tomar» en un proceso que no lo tiene (la v1), son 409 con el motivo
    public async Task Take_IllegalOrNotInItsProcess_Returns409WithTheReason()
    {
        OfficerAna();
        CaseExists();
        _workflow.TakeAsync(Arg.Any<string>(), Arg.Any<CaseActor>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns<CaseDetail>(
                _ => throw new InvalidOperationException("Acción ilegal: está en 'approved'."),
                _ => throw new ArgumentException("'take' no existe en el proceso 'gov.tramite'."));

        var ilegal = await BuildSut().Take("case-1001", null, default);
        var otroProceso = await BuildSut().Take("case-1001", null, default);

        Assert.Contains("approved", Assert.IsType<ConflictObjectResult>(ilegal).Value!.ToString());
        Assert.Contains("gov.tramite", Assert.IsType<ConflictObjectResult>(otroProceso).Value!.ToString());
    }

    [Fact] // happy: tomar dos veces da el expediente como quedó (doble clic, escenario 6 de la US1)
    public async Task Take_Twice_IsIdempotent_WithTheStubEngine()
    {
        OfficerAna();
        var (sut, cases) = BuildWithStubEngine();

        var primera = CaseOf(await sut.Take("case-1001", null, default));
        var segunda = CaseOf(await sut.Take("case-1001", null, default));

        Assert.Equal("in-review", primera.Application.Status);
        Assert.Equal("in-review", segunda.Application.Status);
        Assert.Equal(CaseStatus.EnRevision, cases.FindCase("case-1001")!.Status);
        // Tomado, ya se decide: lo que la consola ofrece ahora son las tres decisiones.
        Assert.Equal(new[] { "approve", "reject", "request-info" }, segunda.Actions);
    }

    [Fact] // filter: en subsanación no se toma — se espera al ciudadano — y se dice por qué
    public async Task Take_InfoRequested_Returns409_WithTheStubEngine()
    {
        OfficerAna();
        var (sut, cases) = BuildWithStubEngine();

        var result = await sut.Take("case-1003", null, default);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Equal(CaseStatus.Subsanacion, cases.FindCase("case-1003")!.Status);
    }

    [Fact] // las acciones del funcionario salen del estado: radicado → tomar; cerrado → nada
    public async Task Case_ServesTheOfficerActionsOfItsState_WithTheStubEngine()
    {
        OfficerAna();
        var (sut, _) = BuildWithStubEngine();

        Assert.Equal(new[] { "take" }, CaseOf(await sut.Case("case-1001", default)).Actions);
        Assert.Empty(CaseOf(await sut.Case("case-1003", default)).Actions);
        Assert.Empty(CaseOf(await sut.Case("case-1004", default)).Actions);
        Assert.False(CaseOf(await sut.Case("case-1001", default)).Stale);
    }

    [Fact] // la consola pide las acciones de la cara del FUNCIONARIO, no las del ciudadano
    public async Task Case_AsksTheEngineForTheOfficerFace()
    {
        OfficerAna();
        CaseExists();
        _workflow.ActionsAsync("case-1001", CaseRoles.Funcionario, Arg.Any<CancellationToken>())
            .Returns(new[] { "take" });

        var dto = CaseOf(await BuildSut().Case("case-1001", default));

        Assert.Equal(new[] { "take" }, dto.Actions);
        await _workflow.DidNotReceive().ActionsAsync(Arg.Any<string>(), CaseRoles.Ciudadano, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Con el proceso caído, ninguna acción —ni de una tabla local— y <c>stale</c> lo dice.
    /// </summary>
    /// <remarks>
    /// Sin la marca, «sin botones» se leería como «no hay nada que hacer» (FR-012: se dice que se
    /// puede reintentar). El expediente se sigue viendo: es del CMS.
    /// </remarks>
    [Fact]
    public async Task Case_ProcessDown_ServesNoActions_AndSaysStale()
    {
        OfficerAna();
        CaseExists();
        _workflow.ActionsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<string>>(_ => throw new InvalidOperationException("Api.Workflow no responde."));

        var dto = CaseOf(await BuildSut().Case("case-1001", default));

        Assert.Empty(dto.Actions);
        Assert.True(dto.Stale);
    }

    [Fact] // el portal pide las acciones de la cara del CIUDADANO para su propio expediente
    public async Task Application_Owner_ServesTheCitizenActions()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(OwnerKey);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
        CaseExists();
        _workflow.ActionsAsync("case-1001", CaseRoles.Ciudadano, Arg.Any<CancellationToken>())
            .Returns(new[] { "resubmit" });

        var result = await BuildSut().Application("case-1001", default);

        var dto = Assert.IsType<GovController.ApplicationDetailResponse>(Assert.IsType<OkObjectResult>(result).Value).Application;
        Assert.Equal(new[] { "resubmit" }, dto.Actions);
        Assert.False(dto.Stale);
        await _workflow.DidNotReceive().ActionsAsync(Arg.Any<string>(), CaseRoles.Funcionario, Arg.Any<CancellationToken>());
    }

    // ══════════ T6 — DESCARGA DE DOCUMENTOS (el binario ya existe de verdad) ══════════
    //
    // El id del fichero es opaco, pero eso NO autoriza: cada descarga comprueba permiso.
    // Lo baja el DUEÑO del expediente o un FUNCIONARIO; nadie más.

    private static readonly System.Guid OwnerKey = System.Guid.NewGuid();
    private const string DocId = "doc_abc123";

    private static CaseDetail CaseWithDocument(string? fileId) => new(
        CaseId: "case-1001",
        Radicado: "SG-2026-001001",
        TramiteId: "trm-cedula",
        TramiteName: "Cédula",
        Citizen: new GovCitizen("Ana", "ana@correo.co", MemberKey: OwnerKey),
        FormData: new Dictionary<string, string>(),
        Documents: new[]
        {
            new CitizenDocumentRef(DocId, "case-1001", "cedula.pdf", "accepted",
                System.DateTimeOffset.UtcNow, FileId: fileId, ContentType: "application/pdf", SizeBytes: 3),
        },
        Status: CaseStatus.Radicado,
        CurrentStage: "Radicada",
        Priority: CasePriority.Normal,
        SlaDaysLeft: 5,
        FeeMinor: 0m,
        Currency: "COP",
        RadicadoAt: System.DateTimeOffset.UtcNow,
        Timeline: System.Array.Empty<CaseTimelineEntry>(),
        Decision: null);

    private void CaseExists(string? fileId = "file-1")
        => _tracking.GetCaseAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(CaseWithDocument(fileId));

    [Fact] // empty: sin sesión no se descarga nada — ni se toca el almacén
    public async Task Download_Anonymous_Returns401_AndNeverReadsTheFile()
    {
        Anonymous();

        var result = await BuildSut().DownloadDocument("case-1001", DocId, default);

        Assert.IsType<UnauthorizedObjectResult>(result);
        await _files.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // EL CASO CENTRAL: otro ciudadano autenticado NO baja el documento ajeno
    public async Task Download_OtherCitizen_Returns403_AndNeverReadsTheFile()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(System.Guid.NewGuid()); // NO es el dueño
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);   // ni funcionario
        CaseExists();

        var result = await BuildSut().DownloadDocument("case-1001", DocId, default);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        await _files.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact] // happy: el dueño baja su documento con su content-type y su nombre
    public async Task Download_Owner_ReturnsTheFile()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(OwnerKey);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);   // dueño, aunque no sea funcionario
        CaseExists();
        _files.ReadAsync(Arg.Any<string>(), "file-1", Arg.Any<CancellationToken>())
            .Returns(new PrivateFileContent(new byte[] { 1, 2, 3 }, "application/pdf", "cedula.pdf"));
        var sut = BuildSut();

        var result = await sut.DownloadDocument("case-1001", DocId, default);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("application/pdf", file.ContentType);
        Assert.Equal("cedula.pdf", file.FileDownloadName);
        Assert.Equal(new byte[] { 1, 2, 3 }, file.FileContents);
        // El navegador no debe adivinar el tipo: un fichero disfrazado no se ejecuta.
        Assert.Equal("nosniff", sut.Response.Headers["X-Content-Type-Options"]);
    }

    [Fact] // happy: el FUNCIONARIO baja el adjunto aunque no sea suyo — lo necesita para decidir
    public async Task Download_Officer_ReturnsTheFile()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(System.Guid.NewGuid()); // no es el dueño
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);    // pero es funcionario
        CaseExists();
        _files.ReadAsync(Arg.Any<string>(), "file-1", Arg.Any<CancellationToken>())
            .Returns(new PrivateFileContent(new byte[] { 9 }, "application/pdf", "cedula.pdf"));

        var result = await BuildSut().DownloadDocument("case-1001", DocId, default);

        Assert.IsType<FileContentResult>(result);
    }

    [Fact] // Documento previo a T6 (metadata sin binario): 404 honesto, no un 500
    public async Task Download_DocumentWithoutFile_Returns404()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(OwnerKey);
        CaseExists(fileId: null);

        var result = await BuildSut().DownloadDocument("case-1001", DocId, default);

        Assert.IsType<NotFoundObjectResult>(result);
    }
}
