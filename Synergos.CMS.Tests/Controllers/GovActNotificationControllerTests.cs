using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// La superficie del acto notificado en <see cref="GovController"/> (HU #62).
/// </summary>
/// <remarks>
/// <para><b>Lo que se prueba acá no es que se envíe: es que el ACCESO quede registrado y que no
/// se pueda leer el acto sin registrarlo.</b> El día que alguien recurre fuera de término, lo que
/// la entidad tiene que poder sostener es cuándo accedió el ciudadano y cómo se supo que era él —
/// no que salió un correo.</para>
///
/// <para><b>El destinatario sale del expediente, nunca del cuerpo</b>, por la misma razón que
/// radicar y listar (ADR 0103): el radicado es secuencial, así que cualquier identificador que
/// viaje en la petición se enumera contando.</para>
/// </remarks>
public sealed class GovActNotificationControllerTests
{
    private static readonly Guid Ciudadano = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Ajeno = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ITramiteCatalogProvider _catalog = Substitute.For<ITramiteCatalogProvider>();
    private readonly IApplicationService _applications = Substitute.For<IApplicationService>();
    private readonly ICaseWorkflowService _workflow = Substitute.For<ICaseWorkflowService>();
    private readonly ICaseTrackingProvider _tracking = Substitute.For<ICaseTrackingProvider>();
    private readonly IDocumentUploadService _documents = Substitute.For<IDocumentUploadService>();
    private readonly IPrivateFileStore _files = Substitute.For<IPrivateFileStore>();
    private readonly IMessagingService _messaging = Substitute.For<IMessagingService>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IGovActNotificationService _notifications = Substitute.For<IGovActNotificationService>();

    private GovController BuildSut() => new(
        _catalog, _applications, _workflow, _tracking, _documents, _files, _messaging, _gate, _notifications)
    {
        ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
    };

    private void Anonimo() => _gate.IsAuthenticated.Returns(false);

    private void ConSesion(Guid key)
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(key);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(false);
    }

    private void Funcionario()
    {
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberKey.Returns(Ajeno);
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private static CaseDetail Expediente(Guid? memberKey) => new(
        CaseId: "case-1001",
        Radicado: "SG-2026-000001",
        TramiteId: "licencia",
        TramiteName: "Licencia de construcción",
        Citizen: new GovCitizen("Ana", "ana@example.com", MemberKey: memberKey),
        FormData: new Dictionary<string, string>(),
        Documents: Array.Empty<CitizenDocumentRef>(),
        Status: CaseStatus.EnRevision,
        CurrentStage: "revisión",
        Priority: CasePriority.Normal,
        SlaDaysLeft: 5,
        FeeMinor: 0m,
        Currency: "COP",
        RadicadoAt: DateTimeOffset.UtcNow.AddDays(-3),
        Timeline: Array.Empty<CaseTimelineEntry>(),
        Decision: null);

    private static GovActNotification Acto(
        DateTimeOffset? opened = null, string? openedWith = null) => new(
        Id: "not_abc",
        CaseId: "case-1001",
        Radicado: "SG-2026-000001",
        Title: "Resolución 1234 de 2026",
        Body: "Se resuelve NEGAR la solicitud.",
        DocumentRef: "doc-9",
        NotifiedAtUtc: DateTimeOffset.UtcNow.AddDays(-1),
        AcknowledgeBeforeUtc: null,
        OpenedAtUtc: opened,
        OpenedBy: opened is null ? null : Ciudadano,
        OpenedWith: openedWith);

    private static readonly GovController.NotifyActRequest Peticion =
        new("case-1001", "Resolución 1234 de 2026", "Se resuelve NEGAR la solicitud.", "doc-9", null);

    // ── Notificar: es del funcionario, y el destinatario sale del expediente ────────────

    [Fact]
    public async Task Notificar_Anonimo_Devuelve401_Y_No_Toca_El_Seam()
    {
        Anonimo();

        var resultado = await BuildSut().Notify(Peticion, default);

        Assert.IsType<UnauthorizedObjectResult>(resultado);
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(
            default!, default!, default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Notificar_Ciudadano_Sin_Rol_Devuelve403()
    {
        ConSesion(Ciudadano);

        var resultado = await BuildSut().Notify(Peticion, default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(resultado).StatusCode);
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(
            default!, default!, default, default!, default!, default, default, default);
    }

    [Fact]
    public async Task Notificar_Expediente_Inexistente_Devuelve404()
    {
        Funcionario();
        _tracking.GetCaseAsync("case-1001", Arg.Any<CancellationToken>()).Returns((CaseDetail?)null);

        Assert.IsType<NotFoundObjectResult>(await BuildSut().Notify(Peticion, default));
    }

    /// <summary>
    /// Un expediente sin Member detrás NO se notifica electrónicamente.
    /// </summary>
    /// <remarks>
    /// Los radicados anteriores a ADR 0103 no tienen ciudadano con sesión. Notificarlos igual
    /// dejaría escrito un término que nadie puede empezar a contar, porque no hay quien pueda
    /// abrir el acto — y parecería que funcionó.
    /// </remarks>
    [Fact]
    public async Task Notificar_Expediente_Sin_Member_Devuelve409_Y_No_Notifica()
    {
        Funcionario();
        _tracking.GetCaseAsync("case-1001", Arg.Any<CancellationToken>()).Returns(Expediente(memberKey: null));

        Assert.IsType<ConflictObjectResult>(await BuildSut().Notify(Peticion, default));
        await _notifications.DidNotReceiveWithAnyArgs().NotifyAsync(
            default!, default!, default, default!, default!, default, default, default);
    }

    /// <summary>
    /// El destinatario y el radicado salen del EXPEDIENTE, no de quien manda la petición.
    /// </summary>
    /// <remarks>
    /// Es el corazón del guard: el funcionario que notifica tiene su propia llave de Member, y si
    /// el seam recibiera ésa, cada acto quedaría notificado al funcionario. Se comprueba contra
    /// <see cref="Ciudadano"/> justamente porque la sesión activa es la de otro.
    /// </remarks>
    [Fact]
    public async Task Notificar_Usa_El_Ciudadano_Del_Expediente_Y_No_La_Sesion_Del_Funcionario()
    {
        Funcionario();
        _tracking.GetCaseAsync("case-1001", Arg.Any<CancellationToken>()).Returns(Expediente(Ciudadano));
        _notifications.NotifyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Acto());

        var payload = Assert.IsType<GovController.ActNotificationResponse>(
            Assert.IsType<OkObjectResult>(await BuildSut().Notify(Peticion, default)).Value);

        Assert.Equal("SG-2026-000001", payload.Notification.Reference);
        await _notifications.Received(1).NotifyAsync(
            "case-1001", "SG-2026-000001", Ciudadano,
            "Resolución 1234 de 2026", "Se resuelve NEGAR la solicitud.",
            "doc-9", null, Arg.Any<CancellationToken>());
    }

    [Fact] // al funcionario sí se le devuelve el cuerpo: es quien lo escribió
    public async Task Notificar_Devuelve_El_Cuerpo_Al_Funcionario_Aunque_No_Este_Abierto()
    {
        Funcionario();
        _tracking.GetCaseAsync("case-1001", Arg.Any<CancellationToken>()).Returns(Expediente(Ciudadano));
        _notifications.NotifyAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string?>(), Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(Acto());

        var payload = Assert.IsType<GovController.ActNotificationResponse>(
            Assert.IsType<OkObjectResult>(await BuildSut().Notify(Peticion, default)).Value);

        Assert.Equal("Se resuelve NEGAR la solicitud.", payload.Notification.Body);
        Assert.False(payload.Notification.Opened);
    }

    // ── La bandeja: lista sin destapar el acto ──────────────────────────────────────────

    [Fact]
    public async Task Bandeja_Anonima_Devuelve401_Y_No_Toca_El_Seam()
    {
        Anonimo();

        Assert.IsType<UnauthorizedObjectResult>(await BuildSut().Notifications(default));
        await _notifications.DidNotReceive().GetForCitizenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// <b>El acto sin abrir se lista, pero no se lee.</b>
    /// </summary>
    /// <remarks>
    /// Es la regla que sostiene toda la HU. Si la bandeja trajera el cuerpo, el ciudadano se
    /// enteraría de lo resuelto sin que nada registrara su acceso: el acuse pasaría a ser un botón
    /// decorativo y el término no empezaría nunca.
    /// </remarks>
    [Fact]
    public async Task Bandeja_No_Trae_El_Cuerpo_Del_Acto_Sin_Abrir()
    {
        ConSesion(Ciudadano);
        _notifications.GetForCitizenAsync(Ciudadano, Arg.Any<CancellationToken>()).Returns(new[] { Acto() });

        var payload = Assert.IsType<GovController.ActNotificationsResponse>(
            Assert.IsType<OkObjectResult>(await BuildSut().Notifications(default)).Value);

        var uno = Assert.Single(payload.Notifications);
        Assert.Equal("Resolución 1234 de 2026", uno.Title);   // el título sí: hay que saber que existe
        Assert.Null(uno.Body);
        Assert.Null(uno.DocumentRef);
        Assert.False(uno.Opened);
    }

    [Fact] // ya abierto, releerlo es gratis: el término ya empezó
    public async Task Bandeja_Trae_El_Cuerpo_Cuando_Ya_Estaba_Abierto()
    {
        ConSesion(Ciudadano);
        _notifications.GetForCitizenAsync(Ciudadano, Arg.Any<CancellationToken>())
            .Returns(new[] { Acto(opened: DateTimeOffset.UtcNow, openedWith: GovActAssertions.CmsSession) });

        var payload = Assert.IsType<GovController.ActNotificationsResponse>(
            Assert.IsType<OkObjectResult>(await BuildSut().Notifications(default)).Value);

        var uno = Assert.Single(payload.Notifications);
        Assert.Equal("Se resuelve NEGAR la solicitud.", uno.Body);
        Assert.True(uno.Opened);
        Assert.Equal(GovActAssertions.CmsSession, uno.OpenedWith);
    }

    [Fact] // la bandeja es la del member de la sesión, y no acepta a quién listar
    public async Task Bandeja_Es_La_Del_Member_De_La_Sesion()
    {
        ConSesion(Ciudadano);
        _notifications.GetForCitizenAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<GovActNotification>());

        await BuildSut().Notifications(default);

        await _notifications.Received(1).GetForCitizenAsync(Ciudadano, Arg.Any<CancellationToken>());
        await _notifications.DidNotReceive().GetForCitizenAsync(Ajeno, Arg.Any<CancellationToken>());
    }

    // ── Abrir: el acceso queda registrado, y sólo lo abre su dueño ──────────────────────

    [Fact]
    public async Task Abrir_Anonimo_Devuelve401_Y_No_Registra_Nada()
    {
        Anonimo();

        Assert.IsType<UnauthorizedObjectResult>(await BuildSut().OpenNotification("not_abc", default));
        await _notifications.DidNotReceive().AcknowledgeAsync(
            Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Abrir_Lo_Ajeno_Devuelve403()
    {
        ConSesion(Ajeno);
        _notifications.AcknowledgeAsync("not_abc", Ajeno, Arg.Any<CancellationToken>())
            .Throws(new GovActNotAddresseeException());

        var resultado = await BuildSut().OpenNotification("not_abc", default);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(resultado).StatusCode);
    }

    /// <summary>
    /// Vencida y ajena son cosas distintas hacia fuera.
    /// </summary>
    /// <remarks>
    /// El seam lanza lo mismo (<c>InvalidOperationException</c>) porque hacia dentro las dos son
    /// «no puedo certificar este acceso», pero un 403 y un 409 le dicen al ciudadano cosas
    /// opuestas sobre qué hacer a continuación.
    /// </remarks>
    [Fact]
    public async Task Abrir_Fuera_De_Plazo_Devuelve409_Y_No_403()
    {
        ConSesion(Ciudadano);
        _notifications.AcknowledgeAsync("not_abc", Ciudadano, Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("El plazo para registrar el acceso venció el 2026-01-01."));

        Assert.IsType<ConflictObjectResult>(await BuildSut().OpenNotification("not_abc", default));
    }

    [Fact]
    public async Task Abrir_Inexistente_Devuelve404()
    {
        ConSesion(Ciudadano);
        _notifications.AcknowledgeAsync("nope", Ciudadano, Arg.Any<CancellationToken>())
            .Throws(new ArgumentException("Notificación 'nope' no encontrada."));

        Assert.IsType<NotFoundObjectResult>(await BuildSut().OpenNotification("nope", default));
    }

    [Fact] // abrir devuelve el acto destapado, con su acceso ya registrado
    public async Task Abrir_Devuelve_El_Acto_Con_El_Acceso_Registrado()
    {
        var cuando = DateTimeOffset.UtcNow;
        ConSesion(Ciudadano);
        _notifications.AcknowledgeAsync("not_abc", Ciudadano, Arg.Any<CancellationToken>())
            .Returns(Acto(opened: cuando, openedWith: GovActAssertions.CmsSession));

        var payload = Assert.IsType<GovController.ActNotificationResponse>(
            Assert.IsType<OkObjectResult>(await BuildSut().OpenNotification("not_abc", default)).Value);

        Assert.Equal("Se resuelve NEGAR la solicitud.", payload.Notification.Body);
        Assert.Equal("doc-9", payload.Notification.DocumentRef);
        Assert.Equal(cuando, payload.Notification.OpenedAt);
        await _notifications.Received(1).AcknowledgeAsync("not_abc", Ciudadano, Arg.Any<CancellationToken>());
    }

    [Fact] // quien abre es el member de la sesión, y el id de la ruta no puede sustituirlo
    public async Task Abrir_Registra_Al_Member_De_La_Sesion_Y_No_A_Otro()
    {
        ConSesion(Ciudadano);
        _notifications.AcknowledgeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Acto(opened: DateTimeOffset.UtcNow, openedWith: GovActAssertions.CmsSession));

        await BuildSut().OpenNotification("not_abc", default);

        await _notifications.DidNotReceive().AcknowledgeAsync(
            Arg.Any<string>(), Ajeno, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// La llave del Member dueño NUNCA sale del servidor.
    /// </summary>
    /// <remarks>
    /// El DTO no la tiene, y es a propósito: es la lección de #47 —el seudónimo del comprador
    /// acabó en pantalla— aplicada antes de que pase. Este test lo fija por reflexión para que
    /// añadirla rompa el build en vez de pasar desapercibido en una revisión.
    /// </remarks>
    [Fact]
    public void El_DTO_del_acto_no_expone_la_llave_del_Member()
    {
        var propiedades = typeof(GovController.ActNotificationDto)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(Guid) || p.PropertyType == typeof(Guid?))
            .Select(p => p.Name)
            .ToList();

        Assert.True(propiedades.Count == 0,
            "El acto notificado no puede llevar la llave de un Member hacia la vista: "
            + string.Join(", ", propiedades));
    }
}
