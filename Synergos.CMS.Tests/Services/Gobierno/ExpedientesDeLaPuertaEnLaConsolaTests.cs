using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Gobierno;
using static Synergos.CMS.Tests.Services.Gobierno.ArtefactoDelExpedienteTests;

namespace Synergos.CMS.Tests.Services.Gobierno;

/// <summary>
/// Los expedientes de la puerta en la consola y en «Mis solicitudes» (009-gobierno-tramite T040): el estado y la historia
/// salen del proceso al leer, las acciones son las de la v2 desde ese estado —o ninguna, marcado, si el proceso no
/// contesta— y un acto por la ruta directa no los mueve. Con el agregado, el motor en proceso y el artefacto de verdad;
/// el orquestador, simulado.
/// </summary>
public sealed class ExpedientesDeLaPuertaEnLaConsolaTests
{
    private static readonly Guid FuncionariaKey = Guid.Parse("5b7c9d1e-2f3a-4b5c-8d6e-7f8091a2b3c4");

    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly OrquestadorDeGobiernoFalso _orquestador = new();
    private readonly ArtefactoDelExpediente _artefacto;
    private readonly StubApplicationService _casos;

    public ExpedientesDeLaPuertaEnLaConsolaTests() => (_artefacto, _casos) = Nuevo(_orquestador);

    private GovController Sut() => new(
        Substitute.For<ITramiteCatalogProvider>(), _casos, new StubCaseWorkflowService(_casos), new StubCaseTrackingProvider(_casos),
        Substitute.For<IDocumentUploadService>(), Substitute.For<IPrivateFileStore>(), Substitute.For<IMessagingService>(),
        _gate, Substitute.For<IGovActNotificationService>(), Substitute.For<IAuditTrailWriter>(), _artefacto)
    {
        ControllerContext = new ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
    };

    private void Funcionaria()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(FuncionariaKey);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private void LaCiudadana()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(Ana);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

    private static GovController.CaseDto CaseOf(IActionResult r)
        => Assert.IsType<GovController.CaseResponse>(Assert.IsType<OkObjectResult>(r).Value).Case;

    private static GovController.ApplicationDetailDto ApplicationOf(IActionResult r)
        => Assert.IsType<GovController.ApplicationDetailResponse>(Assert.IsType<OkObjectResult>(r).Value).Application;

    [Fact]
    public async Task La_consola_ve_el_estado_del_proceso_y_ofrece_lo_de_la_v2_desde_alli()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", "Lo reviso yo."));
        Funcionaria();

        var dto = CaseOf(await Sut().Case(caso, default));

        Assert.Equal("in-review", dto.Application.Status);
        Assert.Equal(["approve", "reject", "request-info"], dto.Actions);
        Assert.False(dto.Stale);
        Assert.Equal(("puerta", "rad-1"), (dto.Application.FilingPath, dto.Application.FilingId));
        Assert.Equal(new string?[] { null }, _orquestador.CarasDelExpediente);
    }

    /// <summary>La proyección decía «radicado» y el proceso «aprobado»: manda el proceso, con su resolución (FR-028).</summary>
    [Fact]
    public async Task El_proceso_manda_sobre_lo_que_decia_la_proyeccion()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "approved",
            ("take", "in-review", "gobierno.funcionario:f-1", null), ("approve", "approved", "gobierno.funcionario:f-2", "Cumple."));
        Funcionaria();

        var dto = CaseOf(await Sut().Case(caso, default));

        Assert.Equal("approved", dto.Application.Status);
        Assert.Empty(dto.Actions);
        Assert.Equal(("approve", "Cumple.", "gobierno.funcionario:f-2"), (dto.Decision!.Outcome, dto.Decision.Note, dto.Decision.DecidedBy));
    }

    /// <summary>US6-4: con el proceso sin contestar, lo último que se sabe, marcado como no actualizado y sin acciones.</summary>
    [Fact]
    public async Task Sin_respuesta_del_proceso_lo_ultimo_que_se_sabe_marcado_y_sin_acciones()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", null));
        Funcionaria();
        await Sut().Case(caso, default);
        _orquestador.ProcesoCaido = true;

        var dto = CaseOf(await Sut().Case(caso, default));

        Assert.Equal("in-review", dto.Application.Status);
        Assert.True(dto.Stale);
        Assert.Empty(dto.Actions);
    }

    [Fact]
    public async Task La_ciudadana_ve_su_expediente_al_dia_con_su_cara_y_la_subsanacion_que_le_toca()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "info-requested",
            ("take", "in-review", "gobierno.funcionario:f-1", null), ("request-info", "info-requested", "gobierno.funcionario:f-1", "Falta el certificado."));
        LaCiudadana();

        var dto = ApplicationOf(await Sut().Application(caso, default));

        Assert.Equal("info-requested", dto.Status);
        Assert.Equal(["resubmit"], dto.Actions);
        Assert.Equal(("puerta", "rad-1"), (dto.FilingPath, dto.FilingId));
        Assert.NotEmpty(_orquestador.CarasDelExpediente);
        Assert.All(_orquestador.CarasDelExpediente, c => Assert.Equal($"gobierno.ciudadano:{Ana:n}", c));
    }

    /// <summary>US6-1 y US6-2: «Mis solicitudes» y la cola muestran el estado del proceso, también lo que avanzó por la puerta.</summary>
    [Fact]
    public async Task Mis_solicitudes_y_la_cola_se_ponen_al_dia_con_el_proceso()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", null));

        LaCiudadana();
        var bandeja = Assert.IsType<GovController.ApplicationsResponse>(Assert.IsType<OkObjectResult>(await Sut().Applications(default)).Value);
        Funcionaria();
        var cola = Assert.IsType<GovController.QueueResponse>(Assert.IsType<OkObjectResult>(await Sut().Queue(null, "in-review", default)).Value);

        Assert.Equal("in-review", Assert.Single(bandeja.Applications, a => a.Id == caso).Status);
        Assert.Single(cola.Cases, c => c.Id == caso);
    }

    /// <summary>
    /// Un expediente de la puerta no se mueve por la ruta directa: su proceso es la instancia del orquestador, y un acto
    /// directo movería otra y separaría lo que ve el portal de lo que dice el proceso.
    /// </summary>
    [Fact]
    public async Task Los_actos_directos_no_mueven_un_expediente_de_la_puerta()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);

        Funcionaria();
        var tomar = await Sut().Take(caso, null, default);
        var decidir = await Sut().Decision(new GovController.DecisionRequest(caso, "approve", "ok"), default);
        LaCiudadana();
        var subsanar = await Sut().Resubmit(caso, null, default);

        Assert.All(new[] { tomar, decidir, subsanar }, r =>
            Assert.Contains("gov.expediente_de_la_puerta", JsonSerializer.Serialize(Assert.IsType<ConflictObjectResult>(r).Value), StringComparison.Ordinal));
        Assert.Equal(CaseStatus.Radicado, _casos.FindCase(caso)!.Status);
    }
}
