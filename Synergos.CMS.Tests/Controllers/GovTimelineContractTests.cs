using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Xunit;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La fecha de un hito del expediente, contra la forma que la UI lee.
/// </summary>
/// <remarks>
/// <para><b>Un hito que todavía no ocurrió no puede salir con fecha.</b>
/// <see cref="CaseTimelineEntry.Date"/> no admite nulo, así que el motor rellena los pendientes
/// con la fecha de RADICACIÓN; el borde la reenviaba tal cual y el ciudadano leía «Decisión ·
/// 24 de junio de 2026» sobre una decisión que nadie tomó. En un expediente administrativo eso
/// no es cosmético: de las fechas del historial dependen los términos, y una que no ocurrió es
/// justo la que no se puede enseñar.</para>
///
/// <para><b>Por qué no lo vio nadie.</b> Los tests de este controller miran el RBAC y los códigos
/// de estado; el DTO se afirmaba contra sí mismo —«el mapper copia <c>e.Date</c>»— que es
/// verdadero y no dice nada. La UI, en cambio, lleva escrito lo contrario desde el primer día:
/// <c>date: entry.date ? formatDate(entry.date) : undefined</c>, y su modelo dice «may be empty
/// for pending nodes». Por eso estos dos casos miran el <b>JSON</b>, no el record.</para>
/// </remarks>
public sealed class GovTimelineContractTests
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

    private static readonly Guid Ciudadano = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Radicacion = new(2026, 6, 24, 9, 0, 0, TimeSpan.Zero);

    private GovController BuildSut() => new(
        _catalog, _applications, _workflow, _tracking, _documents, _files, _messaging, _gate, _notifications, _audit)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private void SesionDelDuenio()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(Ciudadano);
    }

    private CaseDetail Expediente() => new(
        CaseId: "case-1001",
        Radicado: "SG-2026-001001",
        TramiteId: "tr-1",
        TramiteName: "Certificado de residencia",
        Citizen: new GovCitizen("Ana Ruiz", "ana@correo.co", "CC 1", null, Ciudadano),
        FormData: new Dictionary<string, string>(),
        Documents: Array.Empty<CitizenDocumentRef>(),
        Status: CaseStatus.EnRevision,
        CurrentStage: "En revisión",
        Priority: CasePriority.Normal,
        SlaDaysLeft: 5,
        FeeMinor: 0,
        Currency: "COP",
        RadicadoAt: Radicacion,
        Timeline: new[]
        {
            new CaseTimelineEntry("radicado", "Radicado", Radicacion, "done", "Radicada en línea."),
            // Así lo emite el motor: el hito pendiente lleva la fecha de radicación de relleno.
            new CaseTimelineEntry("resuelto", "Decisión", Radicacion, "pending", string.Empty),
        },
        Decision: null);

    private JsonElement TimelineDelExpediente()
    {
        SesionDelDuenio();
        _tracking.GetCaseAsync("case-1001", Arg.Any<CancellationToken>()).Returns(Expediente());
        _messaging.GetInboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MessageThreadSummary>());

        var payload = Assert.IsType<OkObjectResult>(
            BuildSut().Application("case-1001", default).GetAwaiter().GetResult()).Value;
        var json = JsonDocument.Parse(JsonSerializer.Serialize(payload, Web));
        return json.RootElement.GetProperty("application").GetProperty("timeline").Clone();
    }

    [Fact]
    public void HitoPendiente_SaleSinFecha()
    {
        var timeline = TimelineDelExpediente();

        var pendiente = timeline[1];
        Assert.Equal("pending", pendiente.GetProperty("state").GetString());
        // Nulo es «todavía no ocurrió». La fecha de radicación ahí adentro es un hecho inventado.
        Assert.Equal(JsonValueKind.Null, pendiente.GetProperty("date").ValueKind);
    }

    [Fact]
    public void HitoOcurrido_ConservaSuFecha()
    {
        var timeline = TimelineDelExpediente();

        var hecho = timeline[0];
        Assert.Equal("done", hecho.GetProperty("state").GetString());
        Assert.Equal(Radicacion, hecho.GetProperty("date").GetDateTimeOffset());
    }
}
