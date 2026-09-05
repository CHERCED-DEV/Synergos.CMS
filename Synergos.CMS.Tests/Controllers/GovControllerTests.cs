using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
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

    /// <summary>
    /// El controller lleva un <see cref="Microsoft.AspNetCore.Http.DefaultHttpContext"/>
    /// porque la descarga escribe cabeceras de seguridad en <c>Response</c> (nosniff);
    /// sin contexto eso revienta con NullReference y el test no vería el código real.
    /// </summary>
    private GovController BuildSut() => new(
        _catalog, _applications, _workflow, _tracking, _documents, _files, _messaging, _gate, _notifications)
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
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
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
