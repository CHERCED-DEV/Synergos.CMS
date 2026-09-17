using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using NSubstitute;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;

namespace Synergos.CMS.Tests.Controllers;

/// <summary>
/// El cruce de contrato de <c>&lt;synergos-eventos&gt;</c>: estos tests afirman la forma
/// que la UI LEE, no la que el controller decidió emitir.
/// </summary>
/// <remarks>
/// <para><b>Por qué hacen falta.</b> Los tests que ya había ejercitan el controller contra
/// sus propios DTOs —que <c>InstructorCoursesResponse</c>… perdón, que
/// <c>TicketsResponse</c> lleve lo que el controller puso—, así que una clave renombrada
/// pasaba en verde y la vista caía a datos de ejemplo en producción. Es el defecto de fondo
/// que nombra el #102 de Academy.</para>
///
/// <para><b>Se afirma sobre el JSON, no sobre el record.</b> Comparar propiedades de C#
/// no ve un <c>[JsonPropertyName]</c> equivocado ni el camelCase por defecto, que es
/// exactamente donde vive la deriva. Se serializa con
/// <see cref="JsonSerializerDefaults.Web"/>, que es lo que usa MVC sin configuración
/// propia.</para>
///
/// <para><b>Y los caminos de ESCRITURA se prueban desde el JSON que manda la UI</b>, no
/// construyendo el record a mano: el cuerpo se deserializa igual que en el binding, que es
/// donde se perdían —en silencio— el título del evento, el destinatario de una
/// transferencia y el documento del asistente.</para>
/// </remarks>
public sealed class EventosContractShapeTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly IEventCatalogProvider _catalog = Substitute.For<IEventCatalogProvider>();
    private readonly IEventTicketingService _ticketing = Substitute.For<IEventTicketingService>();
    private readonly IEventManagementService _management = Substitute.For<IEventManagementService>();
    private readonly IPriceFormatter _priceFormatter = Substitute.For<IPriceFormatter>();
    private readonly IMemberAccessGate _gate = Substitute.For<IMemberAccessGate>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();

    public EventosContractShapeTests()
    {
        _priceFormatter.Format(Arg.Any<decimal>(), Arg.Any<string?>()).Returns("$ 0");
        _gate.IsAuthenticated.Returns(true);
        _gate.CurrentMemberEmail.Returns("ana@correo.co");
        _gate.HasAnyRole(Arg.Any<string?>()).Returns(true);
    }

    private EventosController BuildSut() => new(
        _catalog, _ticketing, _management, _priceFormatter, _gate, _realtime,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<EventosController>.Instance);

    private static JsonElement Json(IActionResult result)
    {
        var ok = Assert.IsType<OkObjectResult>(result);
        return JsonSerializer.SerializeToElement(ok.Value, Web);
    }

    private static EventSummary Summary(string id = "EVT-1") => new(
        Id: id,
        Slug: "cumbre-tech",
        Title: "Cumbre Tech Bogotá",
        Category: "Conferencia",
        City: "Bogotá",
        Venue: "Centro de Convenciones Ágora",
        StartUtc: new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero),
        ImageUrl: "/media/eventos/cumbre.jpg",
        PriceFrom: 180_000m,
        Currency: "COP",
        Mode: "general");

    private static EventDetail Detail(string id = "EVT-1") => new(
        Summary: Summary(id),
        Description: "La conferencia de tecnología más grande de la región",
        Organizer: "Synergos Labs",
        Tiers: new[] { new EventTier("gen", "General", 180_000m, "COP", 100, 40, 6) },
        SeatMap: null);

    // ── Catálogo ──────────────────────────────────────────────────────────────────

    [Fact] // happy: la tarjeta lee `venueName`, y el borde solo emitía `venue`
    public async Task Events_EmiteVenueName_YNoSoloVenue()
    {
        _catalog.SearchAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new[] { Summary() });
        _catalog.GetEventAsync("EVT-1", Arg.Any<CancellationToken>()).Returns(Detail());

        var evento = Json(await BuildSut().Events(null, null, null, null, default))
            .GetProperty("events")[0];

        Assert.Equal("Centro de Convenciones Ágora", evento.GetProperty("venueName").GetString());
        // Las que ya estaban bien y no se pueden perder al tocar el DTO.
        Assert.Equal(180_000m, evento.GetProperty("fromAmount").GetDecimal());
        Assert.True(evento.TryGetProperty("startsAt", out _));
        Assert.True(evento.TryGetProperty("cover", out _));
        Assert.True(evento.TryGetProperty("badges", out _));
        Assert.True(evento.TryGetProperty("soldPercent", out _));
    }

    // ── "Mis entradas" (SH-10) ────────────────────────────────────────────────────

    [Fact] // happy: la tarjeta de la billetera lee CINCO claves que el borde no emitía
    public async Task Tickets_EmiteEventTitleVenueNameStartsAtYHolder()
    {
        _ticketing.GetTicketsAsync("ana@correo.co", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new EventTicket("tkt-1", "qr-1", "EVT-1", "Ana Ruiz", "General", null, "ana@correo.co", "valid"),
            });
        _catalog.GetEventAsync("EVT-1", Arg.Any<CancellationToken>()).Returns(Detail());

        var ticket = Json(await BuildSut().Tickets(default)).GetProperty("tickets")[0];

        Assert.Equal("Cumbre Tech Bogotá", ticket.GetProperty("eventTitle").GetString());
        Assert.Equal("Centro de Convenciones Ágora", ticket.GetProperty("venueName").GetString());
        Assert.StartsWith("2026-08-14", ticket.GetProperty("startsAt").GetString());
        Assert.Equal("ana@correo.co", ticket.GetProperty("holder").GetString());
        Assert.Equal("Ana Ruiz", ticket.GetProperty("attendee").GetString());
    }

    [Fact] // empty: evento fuera del catálogo → claves vacías, NO una fecha inventada
    public async Task Tickets_EventoDesconocido_EmiteVacioYNoInventa()
    {
        _ticketing.GetTicketsAsync("ana@correo.co", Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new EventTicket("tkt-1", "qr-1", "EVT-9", "Ana Ruiz", "General", null, "ana@correo.co", "valid"),
            });
        _catalog.GetEventAsync("EVT-9", Arg.Any<CancellationToken>()).Returns((EventDetail?)null);

        var ticket = Json(await BuildSut().Tickets(default)).GetProperty("tickets")[0];

        Assert.Equal(string.Empty, ticket.GetProperty("eventTitle").GetString());
        Assert.Equal(string.Empty, ticket.GetProperty("startsAt").GetString());
    }

    // ── Consola del organizador ───────────────────────────────────────────────────

    [Fact] // happy: la tabla lee `state`, no `checkedIn` — daba a todos por pendientes
    public async Task Manage_EmiteStateCheckedIn_ParaQuienYaEntro()
    {
        _management.GetManageAsync("EVT-1", Arg.Any<CancellationToken>())
            .Returns(new EventManageView("EVT-1", new[]
            {
                new EventAttendee("tkt-1", "Ana Ruiz", "ana@correo.co", "General", null, CheckedIn: true),
                new EventAttendee("tkt-2", "Beto Paz", "beto@correo.co", "General", null, CheckedIn: false),
            }, 100, 2));

        var asistentes = Json(await BuildSut().Manage("EVT-1", default)).GetProperty("attendees");

        Assert.Equal("checked-in", asistentes[0].GetProperty("state").GetString());
        Assert.Equal("pending", asistentes[1].GetProperty("state").GetString());
    }

    [Fact] // happy: el registro de escaneos lee ticketId + attendee; el seam YA los tiene
    public async Task CheckIn_EmiteTicketIdYAttendee()
    {
        _management.CheckInAsync("qr-1", Arg.Any<CancellationToken>())
            .Returns(new EventCheckInResult("valid", "EVT-1", "tkt-1", "Ana Ruiz"));

        var body = Json(await BuildSut().CheckIn(
            JsonSerializer.Deserialize<EventosController.CheckInRequest>("""{"ticketId":"qr-1"}""", Web),
            default));

        Assert.Equal("valid", body.GetProperty("status").GetString());
        Assert.Equal("tkt-1", body.GetProperty("ticketId").GetString());
        Assert.Equal("Ana Ruiz", body.GetProperty("attendee").GetString());
    }

    // ── Escritura: transferir una entrada ─────────────────────────────────────────

    [Fact] // happy: el cuerpo REAL de la UI es { to }; con `toEmail` obligatorio era 400 siempre
    public async Task Transfer_AceptaElCuerpoQueLaUiManda_YEmiteLasClavesRaiz()
    {
        var transferida = new EventTicket("tkt-1", "qr-2", "EVT-1", "Ana Ruiz", "General", null, "beto@correo.co", "transferred");
        _ticketing.GetTicketsAsync("ana@correo.co", Arg.Any<CancellationToken>())
            .Returns(new[] { new EventTicket("tkt-1", "qr-1", "EVT-1", "Ana Ruiz", "General", null, "ana@correo.co", "valid") });
        _ticketing.TransferTicketAsync("tkt-1", "beto@correo.co", Arg.Any<CancellationToken>())
            .Returns(new EventTicketTransferResult(transferida, "qr-2"));
        _catalog.GetEventAsync("EVT-1", Arg.Any<CancellationToken>()).Returns(Detail());

        // El cuerpo tal cual lo serializa `transfer()` del cliente Angular.
        var request = JsonSerializer.Deserialize<EventosController.TransferRequest>(
            """{"to":"beto@correo.co"}""", Web);

        var body = Json(await BuildSut().TransferTicket("tkt-1", request, default));

        await _ticketing.Received(1).TransferTicketAsync("tkt-1", "beto@correo.co", Arg.Any<CancellationToken>());
        Assert.Equal("transferred", body.GetProperty("status").GetString());
        Assert.Equal("beto@correo.co", body.GetProperty("to").GetString());
        Assert.Equal("tkt-1", body.GetProperty("ticketId").GetString());
    }

    // ── Escritura: publicar un evento (wizard SH-6) ───────────────────────────────

    [Fact] // happy: el wizard manda title/venueName/startsAt/amount y NADA de eso se ligaba
    public async Task CreateEvent_AceptaElBorradorQueLaUiManda()
    {
        _management.CreateEventAsync(Arg.Any<EventDraft>(), Arg.Any<CancellationToken>())
            .Returns(new EventCreateResult("EVT-1"));
        _catalog.GetEventAsync("EVT-1", Arg.Any<CancellationToken>()).Returns(Detail());

        // El cuerpo tal cual lo serializa `createEvent()` del cliente Angular
        // (CreateEventRequest del eventos.model.ts).
        var request = JsonSerializer.Deserialize<EventosController.EventDraftRequest>(
            """
            {
              "title": "Cumbre Tech Bogotá",
              "category": "Conferencia",
              "city": "Bogotá",
              "venueName": "Centro de Convenciones Ágora",
              "startsAt": "2026-08-14T09:00:00+00:00",
              "mode": "general",
              "capacity": 500,
              "tiers": [{ "name": "General", "amount": 180000, "capacity": 100 }]
            }
            """, Web);

        var body = Json(await BuildSut().CreateEvent(request, default));

        var draft = _management.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IEventManagementService.CreateEventAsync))
            .GetArguments()[0] as EventDraft;
        Assert.NotNull(draft);
        Assert.Equal("Cumbre Tech Bogotá", draft!.Name);
        Assert.Equal("Centro de Convenciones Ágora", draft.Venue);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero), draft.Date);
        Assert.Equal(180_000m, Assert.Single(draft.Tiers).Price);

        // Y la respuesta: sin `id` el normalizador del cliente da el POST por caído.
        Assert.Equal("EVT-1", body.GetProperty("id").GetString());
        Assert.Equal("cumbre-tech", body.GetProperty("slug").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("status").GetString()));
    }

    // ── Escritura: checkout ───────────────────────────────────────────────────────

    [Fact] // happy: el documento del asistente llega como `document` y se descartaba
    public async Task Checkout_ConservaElDocumentoDelAsistente()
    {
        _ticketing.CheckoutAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<EventCheckoutItem>>(),
                Arg.Any<IReadOnlyList<EventAttendeeInfo>>(),
                Arg.Any<EventBuyerInfo?>(),
                Arg.Any<CancellationToken>())
            .Returns(new EventCheckoutResult("evord_1", "psp_1", 180_000m, "COP"));

        var request = JsonSerializer.Deserialize<EventosController.CheckoutRequest>(
            """
            {
              "eventId": "EVT-1",
              "items": [{ "tier": "gen", "qty": 1 }],
              "attendees": [{ "name": "Ana Ruiz", "email": "ana@correo.co", "document": "CC 1020304050" }],
              "buyer": { "name": "Ana Ruiz", "email": "ana@correo.co" }
            }
            """, Web);

        var body = Json(await BuildSut().Checkout(request, default));

        var attendees = _ticketing.ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == nameof(IEventTicketingService.CheckoutAsync))
            .GetArguments()[2] as IReadOnlyList<EventAttendeeInfo>;
        Assert.Equal("CC 1020304050", Assert.Single(attendees!).DocumentId);
        Assert.False(body.GetProperty("free").GetBoolean());
    }
}
