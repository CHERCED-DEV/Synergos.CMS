using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services.Gobierno;
using Synergos.CMS.Web.Services.Puerta;
using static Synergos.CMS.Tests.Services.Gobierno.ArtefactoDelExpedienteTests;

namespace Synergos.CMS.Tests.Services.Gobierno;

/// <summary>
/// Los expedientes de la puerta en la consola y en «Mis solicitudes» (009-gobierno-tramite T040 y T039): el estado y la
/// historia salen del proceso al leer, las acciones son las de la v2 desde ese estado —o ninguna, marcado, si el proceso
/// no contesta— y tomar, decidir y subsanar uno de ellos va por la puerta y vuelve puesto al día. Con el agregado, el
/// motor en proceso, el artefacto y la puerta de verdad (la tabla incrustada y las entradas del <c>appsettings.json</c>);
/// el orquestador, simulado.
/// </summary>
public sealed class ExpedientesDeLaPuertaEnLaConsolaTests
{
    private static readonly Guid FuncionariaKey = Guid.Parse("5b7c9d1e-2f3a-4b5c-8d6e-7f8091a2b3c4");

    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly OrquestadorDeGobiernoFalso _orquestador = new();
    private readonly ArtefactoDelExpediente _artefacto;
    private readonly StubApplicationService _casos;
    private readonly ActosDeLaPuerta _actos;

    public ExpedientesDeLaPuertaEnLaConsolaTests()
    {
        (_artefacto, _casos) = Nuevo(_orquestador);
        var tabla = TablaDeLaPuerta.Incrustada();
        var monitor = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        monitor.CurrentValue.Returns(new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepoRoot(), "Synergos.CMS.Web", "appsettings.json"), optional: false)
            .Build().GetSection("Synergos:Puerta").Get<PuertaSettings>()!);
        _actos = new ActosDeLaPuerta(
            tabla, new PuertaDeLosFlujos(tabla, monitor, new DestinosDeLaPuerta(["Gobierno"])),
            new ReenvioDeLaPuerta(_orquestador, NullLogger<ReenvioDeLaPuerta>.Instance), _gate, monitor);
    }

    private GovController Sut(string? llave = null)
    {
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        if (llave is not null) http.Request.Headers["Idempotency-Key"] = llave;
        return new(
            Substitute.For<ITramiteCatalogProvider>(), _casos, new StubCaseWorkflowService(_casos), new StubCaseTrackingProvider(_casos),
            Substitute.For<IDocumentUploadService>(), Substitute.For<IPrivateFileStore>(), Substitute.For<IMessagingService>(),
            _gate, Substitute.For<IGovActNotificationService>(), Substitute.For<IAuditTrailWriter>(), _artefacto, _actos)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

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

    // ── Los actos de un expediente de la puerta van por la puerta (T039) ─────────────────────

    /// <summary>
    /// Tomar un expediente de la puerta sale al flujo posterior <c>gobierno.toma</c> con la persona de la funcionaria y la
    /// llave de su intención atada a ella —no por la ruta directa, que movería otra instancia—, y la consola recibe el
    /// expediente puesto al día con el proceso.
    /// </summary>
    [Fact]
    public async Task Tomar_un_expediente_de_la_puerta_va_por_la_puerta_y_vuelve_al_dia()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "submitted");
        Funcionaria();

        var dto = CaseOf(await Sut(llave: "toma-k1").Take(caso, new GovController.TakeRequest("Lo reviso yo."), default));

        var (flujo, sujeto, llave, cuerpo) = Assert.Single(_orquestador.Actos);
        Assert.Equal(("gobierno.toma", $"gobierno.funcionario:{FuncionariaKey:n}"), (flujo, sujeto));
        Assert.Equal(LoQuePoneLaPuerta.LlaveReemitida(sujeto!, flujo, "toma-k1"), llave);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(("rad-1", "Lo reviso yo."), (json.RootElement.GetProperty("radicacion").GetString(), json.RootElement.GetProperty("nota").GetString()));
        Assert.Equal("in-review", dto.Application.Status);
        Assert.Equal(["approve", "reject", "request-info"], dto.Actions);
    }

    [Fact]
    public async Task Decidir_un_expediente_de_la_puerta_va_por_la_puerta_con_la_decision_y_su_nota()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "in-review", ("take", "in-review", "gobierno.funcionario:f-1", null));
        Funcionaria();

        var dto = CaseOf(await Sut(llave: "decision-k1").Decision(new GovController.DecisionRequest(caso, "approve", "Cumple."), default));

        var (flujo, _, _, cuerpo) = Assert.Single(_orquestador.Actos);
        Assert.Equal("gobierno.decision", flujo);
        using var json = JsonDocument.Parse(cuerpo);
        Assert.Equal(("approve", "Cumple."), (json.RootElement.GetProperty("decision").GetString(), json.RootElement.GetProperty("nota").GetString()));
        Assert.Equal("approved", dto.Application.Status);
        Assert.Equal("Cumple.", dto.Decision!.Note);
    }

    [Fact]
    public async Task La_ciudadana_subsana_su_expediente_de_la_puerta_por_la_puerta_con_su_sujeto()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "info-requested",
            ("take", "in-review", "gobierno.funcionario:f-1", null), ("request-info", "info-requested", "gobierno.funcionario:f-1", "Falta el certificado."));
        LaCiudadana();

        var r = await Sut(llave: "subsanacion-k1").Resubmit(caso, new GovController.ResubmitRequest("Adjunto el certificado."), default);

        var (flujo, sujeto, _, _) = Assert.Single(_orquestador.Actos);
        Assert.Equal(("gobierno.subsanacion", $"gobierno.ciudadano:{Ana:n}"), (flujo, sujeto));
        Assert.Equal("in-review", ApplicationOf(r).Status);
    }

    /// <summary>El «no» del proceso pasa con su código —el elemento dice «el expediente ya cambió»— y nada cambia aquí.</summary>
    [Fact]
    public async Task Un_rechazo_del_proceso_pasa_con_su_codigo_y_el_expediente_no_cambia()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        _orquestador.ConProceso("rad-1", "submitted");
        _orquestador.ActoRechaza = "workflow.transition_not_allowed";
        Funcionaria();

        var r = Assert.IsType<ContentResult>(await Sut(llave: "toma-k1").Take(caso, null, default));

        Assert.Equal(409, r.StatusCode);
        Assert.Equal("workflow.transition_not_allowed", JsonDocument.Parse(r.Content!).RootElement.GetProperty("code").GetString());
        Assert.Equal(CaseStatus.Radicado, _casos.FindCase(caso)!.Status);
    }

    /// <summary>Sin la llave de la intención el acto no sale: un doble clic sería otro acto.</summary>
    [Fact]
    public async Task Sin_la_llave_de_la_intencion_el_acto_no_sale()
    {
        var caso = await Confirmado(_artefacto, _casos, _orquestador);
        Funcionaria();

        var r = Assert.IsType<ContentResult>(await Sut().Take(caso, null, default));

        Assert.Equal(400, r.StatusCode);
        Assert.Equal("puerta.llave_requerida", JsonDocument.Parse(r.Content!).RootElement.GetProperty("code").GetString());
        Assert.Empty(_orquestador.Actos);
    }
}
