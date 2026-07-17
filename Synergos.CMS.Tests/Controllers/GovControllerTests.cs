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
    private readonly IMessagingService _messaging = Substitute.For<IMessagingService>();
    private readonly IGovFeeCalculator _fees = Substitute.For<IGovFeeCalculator>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();

    private GovController BuildSut() => new(
        _catalog, _applications, _workflow, _tracking, _documents, _messaging, _fees, _priceFormatter, _gate);

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
}
