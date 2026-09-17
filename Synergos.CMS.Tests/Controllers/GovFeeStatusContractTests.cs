using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El estado del cobro de la tasa, contra la forma que la UI lee.
/// </summary>
/// <remarks>
/// <para><b>El expediente lo ESCRIBÍA y ninguna superficie lo devolvía</b> (HU #116). El
/// agregado guarda <c>CaseState.PaymentStatus</c> desde la ADR 0116 fase 5 y
/// <see cref="CaseDetail"/> —la única proyección que llega a las dos bandejas— no lo
/// declaraba: una escritura sin camino de lectura, el espejo de
/// <c>feedback_no_read_without_a_write_path</c>. Con el motor de pago en proceso daba igual,
/// porque contestaba siempre <c>Captured</c>; con <c>Api.Payments</c> detrás, un
/// <c>unavailable</c> es exactamente el caso que alguien tendría que perseguir.</para>
///
/// <para><b>Por qué no lo vio nadie.</b> Los tests del motor miraban el ALMACÉN —«¿quedó
/// escrito que no se pudo cobrar?»— y los del borde miraban el RBAC y los códigos de estado.
/// Nadie cruzaba las dos mitades, que es justo donde estaba el hueco: el dato existía en
/// disco y no salía por ningún endpoint.</para>
///
/// <para><b>El fixture lleva el caso que el default NO produce</b> — un expediente con tasa
/// que no se cobró. Con todos los expedientes exentos, o todos cobrados, emitir la clave o no
/// daría exactamente el mismo JSON.</para>
/// </remarks>
public sealed class GovFeeStatusContractTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

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

    private static readonly Guid Ciudadano = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Radicacion = new(2026, 7, 2, 15, 20, 0, TimeSpan.Zero);

    private GovController BuildSut() => new(
        _catalog, _applications, _workflow, _tracking, _documents, _files, _messaging, _gate, _notifications, _audit)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private static CaseDetail Expediente(decimal tasa, string? estadoDelCobro) => new(
        CaseId: "case-2001",
        Radicado: "SG-2026-002001",
        TramiteId: "tr-matricula",
        TramiteName: "Renovación de matrícula mercantil",
        Citizen: new GovCitizen("Andrés Castro", "andres@correo.co", "CC 9", null, Ciudadano),
        FormData: new Dictionary<string, string>(),
        Documents: Array.Empty<CitizenDocumentRef>(),
        Status: CaseStatus.Radicado,
        CurrentStage: "Radicada",
        Priority: CasePriority.High,
        SlaDaysLeft: 1,
        FeeMinor: tasa,
        Currency: "COP",
        RadicadoAt: Radicacion,
        Timeline: Array.Empty<CaseTimelineEntry>(),
        Decision: null,
        FeeStatus: estadoDelCobro);

    private void SesionDelDuenio()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(Ciudadano);
    }

    private void SesionDeFuncionario()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.HasAnyRole(Arg.Any<string>()).Returns(true);
    }

    private static JsonElement Json(IActionResult result)
    {
        var payload = Assert.IsType<OkObjectResult>(result).Value;
        return JsonDocument.Parse(JsonSerializer.Serialize(payload, Web)).RootElement.Clone();
    }

    private JsonElement SeguimientoDelCiudadano(decimal tasa, string? estado)
    {
        SesionDelDuenio();
        _tracking.GetCaseAsync("case-2001", Arg.Any<CancellationToken>()).Returns(Expediente(tasa, estado));
        _messaging.GetInboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MessageThreadSummary>());

        return Json(BuildSut().Application("case-2001", default).GetAwaiter().GetResult())
            .GetProperty("application");
    }

    [Fact] // happy: la tasa que no se cobró SALE, que es lo que no salía de ninguna parte.
    public void ElSeguimientoDelCiudadano_TraeElEstadoDelCobro()
    {
        var app = SeguimientoDelCiudadano(42_000m, "Unavailable");

        Assert.Equal("unavailable", app.GetProperty("feeStatus").GetString());
        Assert.Equal(42_000L, app.GetProperty("feeMinor").GetInt64());
    }

    [Fact] // vacío: sin estado, nulo — que es «no consta» y NO «cobrada».
    public void SinEstadoDelCobro_SaleNuloYNoSeRellena()
    {
        var app = SeguimientoDelCiudadano(42_000m, null);

        // La clave se emite DECLARADA aunque no haya dato: quitarla deja al normalizador del
        // otro árbol poniendo su propio valor, y el suyo sería «sin tasa pendiente».
        Assert.True(app.TryGetProperty("feeStatus", out var estado));
        Assert.Equal(JsonValueKind.Null, estado.ValueKind);
        // Y el monto acompaña: es lo que distingue «exento» de «no se sabe».
        Assert.Equal(42_000L, app.GetProperty("feeMinor").GetInt64());
    }

    [Fact] // filtro: el nombre del motor de pago se traduce al slug que el resto del borde usa.
    public void ElEstadoQueExigeIrAPagar_ViajaComoSlug()
    {
        var app = SeguimientoDelCiudadano(42_000m, "RequiresAction");

        Assert.Equal("requires-action", app.GetProperty("feeStatus").GetString());
    }

    [Fact] // happy: y la COLA también, que es donde se persigue un cobro que no salió.
    public async Task LaColaDelFuncionario_TraeElEstadoDelCobro()
    {
        SesionDeFuncionario();
        _tracking.GetQueueAsync(null, null, Arg.Any<CancellationToken>()).Returns(new[]
        {
            new CaseInboxItem("case-2001", "SG-2026-002001", "Renovación de matrícula mercantil",
                "Andrés Castro", CaseStatus.Radicado, "Radicada", CasePriority.High, 1, Radicacion,
                FeeMinor: 42_000m, FeeStatus: "Unavailable"),
            new CaseInboxItem("case-2002", "SG-2026-002002", "Certificado de residencia",
                "Ana Ruiz", CaseStatus.EnRevision, "En revisión", CasePriority.Normal, 5, Radicacion),
        });

        var cases = Json(await BuildSut().Queue(null, null, default))
            .GetProperty("cases");

        Assert.Equal("unavailable", cases[0].GetProperty("feeStatus").GetString());
        Assert.Equal(42_000L, cases[0].GetProperty("feeMinor").GetInt64());
        // El exento sale nulo y con tasa cero: las dos cosas juntas dicen «no hay cobro».
        Assert.Equal(JsonValueKind.Null, cases[1].GetProperty("feeStatus").ValueKind);
        Assert.Equal(0L, cases[1].GetProperty("feeMinor").GetInt64());
    }

    [Fact] // happy: y el expediente del funcionario, donde se decide.
    public async Task ElExpedienteDelFuncionario_TraeElEstadoDelCobro()
    {
        SesionDeFuncionario();
        _tracking.GetCaseAsync("case-2001", Arg.Any<CancellationToken>())
            .Returns(Expediente(42_000m, "Unavailable"));
        _catalog.GetAsync("tr-matricula", Arg.Any<CancellationToken>()).Returns((TramiteDetail?)null);
        _notifications.GetForCaseAsync("case-2001", Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GovActNotification>());

        var app = Json(await BuildSut().Case("case-2001", default))
            .GetProperty("case").GetProperty("application");

        Assert.Equal("unavailable", app.GetProperty("feeStatus").GetString());
        Assert.Equal(42_000L, app.GetProperty("feeMinor").GetInt64());
    }
}
