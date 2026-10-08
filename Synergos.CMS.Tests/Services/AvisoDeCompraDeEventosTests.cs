using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El aviso «entradas confirmadas» le llega a QUIEN COMPRÓ, no al primer asistente, por los dos
/// caminos de compra.
/// </summary>
/// <remarks>
/// <para><b>El defecto:</b> el checkout ya abría la sesión de pago a nombre del comprador (#107),
/// pero la orden no lo guardaba, así que al confirmar <see cref="EventPurchaseNotification"/>
/// tomaba la primera unidad. Quien compra dos entradas para otros pagaba y no recibía la
/// confirmación: le llegaba a un invitado. Ningún test miraba este aviso, porque los de los dos
/// motores confirmaban sin notificador.</para>
///
/// <para><b>Los dos modos pasan por la MISMA teoría</b> porque los dos emiten por
/// <see cref="EventPurchaseNotification"/> y los dos tienen que guardar al comprador: si uno lo
/// olvida vuelve a avisarle al primer asistente, y el otro no lo delata.</para>
///
/// <para><b>El fixture EXIGE la regla porque el comprador NO está entre los asistentes</b>, como en
/// <c>Checkout_ConComprador_AbreLaSesionASuNombre</c>: con un comprador que además va primero en
/// la lista, el test pasaría en verde con el defecto puesto.</para>
/// </remarks>
public sealed class AvisoDeCompraDeEventosTests
{
    public static TheoryData<string> Modos => new() { "Stub", "Bff" };

    private static readonly DateTimeOffset Hoy = HttpEventTicketingServiceTests.Hoy;

    private static readonly ITicketSigner Firmante =
        new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-del-aviso-de-compra"));

    private static readonly IReadOnlyList<EventCheckoutItem> DosGenerales =
        new[] { new EventCheckoutItem("GEN", null, 2) };

    private static readonly IReadOnlyList<EventAttendeeInfo> Dos = new[]
    {
        new EventAttendeeInfo("Ana Asistente", "ana@ejemplo.co", "1001"),
        new EventAttendeeInfo("Beto Asistente", "beto@ejemplo.co", "1002"),
    };

    private static readonly EventBuyerInfo QuienRegala = new("Quien Regala", "regala@ejemplo.co");

    /// <summary>
    /// Lo que se le pasó al notificador, y si hay un siguiente, se lo entrega.
    /// </summary>
    /// <remarks>
    /// Es canal y notificador a la vez: sin siguiente sirve de canal del despachador real, y con
    /// él deja ver lo que el motor EMITIÓ además de lo que se ENTREGÓ.
    /// </remarks>
    private sealed class Buzon(ITransactionalNotifier? siguiente = null) : ITransactionalNotifierChannel
    {
        public List<NotificationEvent> Recibidos { get; } = new();

        public Task DispatchAsync(NotificationEvent notification, CancellationToken cancellationToken = default)
        {
            Recibidos.Add(notification);
            return siguiente?.DispatchAsync(notification, cancellationToken) ?? Task.CompletedTask;
        }
    }

    /// <summary>
    /// Un orquestador mínimo: aparta lo que se le pide y confirma lo que abrió. Anota todo lo que
    /// le llega —ruta, cabeceras y cuerpo— para poder afirmar que el correo del comprador no viaja.
    /// </summary>
    private sealed class Orquestador : HttpMessageHandler
    {
        private const string Raiz = "/v1/ticket-purchases";
        private readonly Dictionary<string, JsonObject> _sagas = new(StringComparer.Ordinal);

        public List<string> Cable { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;
            var cuerpo = req.Content is null ? string.Empty : await req.Content.ReadAsStringAsync(ct);
            Cable.Add($"{req.Method} {req.RequestUri} "
                + string.Join(" ", req.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}"))
                + $" {cuerpo}");

            if (req.Method == HttpMethod.Post && path == Raiz)
            {
                var llave = req.Headers.GetValues("Idempotency-Key").Single();
                if (!_sagas.TryGetValue(llave, out var saga))
                {
                    saga = new JsonObject
                    {
                        ["id"] = llave,
                        ["status"] = "Running",
                        ["total"] = new JsonObject { ["amount"] = 240000, ["currency"] = "COP" },
                        ["held"] = JsonNode.Parse(JsonNode.Parse(cuerpo)!["lines"]!.ToJsonString()),
                        ["pendingCompensations"] = 0,
                    };
                    _sagas[llave] = saga;
                }
                return Responder(HttpStatusCode.Created, saga);
            }

            if (req.Method == HttpMethod.Post
                && path.StartsWith(Raiz + "/", StringComparison.Ordinal)
                && path.EndsWith("/confirm", StringComparison.Ordinal)
                && _sagas.TryGetValue(Uri.UnescapeDataString(path[(Raiz.Length + 1)..^"/confirm".Length]), out var abierta))
            {
                abierta["status"] = "Completed";
                return Responder(HttpStatusCode.OK, abierta);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Responder(HttpStatusCode codigo, JsonObject saga) => new(codigo)
        {
            Content = new StringContent(saga.ToJsonString(), Encoding.UTF8, "application/json"),
        };
    }

    private sealed class Fabrica(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://eventos.local/") };
    }

    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    /// <summary>Un camino de compra, el evento que vende y, si sale a la red, su orquestador.</summary>
    private sealed record Motor(IEventTicketingService Svc, string EventId, Orquestador? Red);

    private static Motor Nuevo(string modo, ITransactionalNotifier avisos)
    {
        if (modo == "Stub")
        {
            return new Motor(
                new StubEventTicketingService(
                    new StubEventCatalogProvider(),
                    new StubReservationService(),
                    new StubPaymentProvider(),
                    tracking: null,
                    audit: null,
                    store: null,
                    now: () => StubEventTicketingServiceTests.EnVenta,
                    notifier: avisos,
                    signer: Firmante),
                "evt-festival-estereo",
                null);
        }

        var red = new Orquestador();
        return new Motor(
            new HttpEventTicketingService(
                new Fabrica(red),
                new Monitor<EventosSettings>(new EventosSettings { Mode = "Bff" }),
                new EventTicketLedger(signer: Firmante),
                HttpEventTicketingServiceTests.Cartelera(),
                NullLogger<HttpEventTicketingService>.Instance,
                notifier: avisos,
                now: () => Hoy),
            "evt-1",
            red);
    }

    private static async Task ComprarYConfirmarAsync(Motor motor, EventBuyerInfo? comprador, int confirmaciones = 1)
    {
        var compra = await motor.Svc.CheckoutAsync(motor.EventId, DosGenerales, Dos, comprador);
        for (var i = 0; i < confirmaciones; i++)
        {
            await motor.Svc.ConfirmAsync(compra.OrderRef);
        }
    }

    // ── Por los dos caminos de compra ───────────────────────────────────────

    /// <summary>
    /// happy: el aviso es del comprador, uno solo y con todas las entradas, aunque no vaya.
    /// </summary>
    /// <remarks>
    /// Confirmar vuelve a leer la orden del registro, así que esto prueba también que el comprador
    /// sobrevive al JSON que se escribe. Y en el modo Bff, que su correo se queda de este lado: lo
    /// que viaja es el seudónimo.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Modos))]
    public async Task El_aviso_de_la_compra_le_llega_al_comprador_y_no_al_primer_asistente(string modo)
    {
        var buzon = new Buzon();
        var motor = Nuevo(modo, buzon);

        await ComprarYConfirmarAsync(motor, QuienRegala);

        var aviso = Assert.Single(buzon.Recibidos);
        Assert.Equal(NotificationTypes.EventTicketsConfirmed, aviso.Type);
        Assert.Equal("regala@ejemplo.co", aviso.ToEmail);
        Assert.Equal("Quien Regala", aviso.ToName);
        Assert.Equal(2, aviso.Lines!.Count);
        Assert.DoesNotContain(motor.Red?.Cable ?? new List<string>(),
            linea => linea.Contains("regala@ejemplo.co", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// filter: sin comprador declarado, el comprador es el primer asistente — el supuesto de siempre.
    /// </summary>
    /// <remarks>
    /// Es el mismo que usa la sesión de pago, y lo que mantiene funcionando a los consumidores
    /// que no lo mandan. El segundo asistente no recibe nada.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Modos))]
    public async Task Sin_comprador_declarado_el_aviso_es_del_primer_asistente(string modo)
    {
        var buzon = new Buzon();
        var motor = Nuevo(modo, buzon);

        await ComprarYConfirmarAsync(motor, comprador: null);

        var aviso = Assert.Single(buzon.Recibidos);
        Assert.Equal("ana@ejemplo.co", aviso.ToEmail);
        Assert.Equal("Ana Asistente", aviso.ToName);
    }

    /// <summary>
    /// idempotent: confirmar dos veces vuelve a emitir —a propósito—, al MISMO comprador y con la
    /// misma llave, y el despachador real entrega uno solo.
    /// </summary>
    /// <remarks>
    /// La re-emisión es lo que rescata un primer aviso perdido (ADR 0106), así que no basta con
    /// contar entregas: la segunda vuelta lee la orden ya confirmada, y si ahí el comprador se
    /// perdiera, la llave la suprimiría y nadie lo vería hasta el día en que la primera falle.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Modos))]
    public async Task Confirmar_dos_veces_emite_al_comprador_y_entrega_un_solo_aviso(string modo)
    {
        var entregados = new Buzon();
        var emitidos = new Buzon(new CompositeTransactionalNotifier(
            new[] { entregados },
            new InMemoryIdempotencyLedger(),
            new RecordingAuditTrailWriter(),
            new Monitor<NotificationsSettings>(new NotificationsSettings { Enabled = true }),
            NullLogger<CompositeTransactionalNotifier>.Instance));
        var motor = Nuevo(modo, emitidos);

        await ComprarYConfirmarAsync(motor, QuienRegala, confirmaciones: 2);

        Assert.Equal(2, emitidos.Recibidos.Count);
        Assert.All(emitidos.Recibidos, a => Assert.Equal("regala@ejemplo.co", a.ToEmail));
        Assert.Single(emitidos.Recibidos.Select(a => a.ResolvedDedupeKey).Distinct());
        Assert.Equal("regala@ejemplo.co", Assert.Single(entregados.Recibidos).ToEmail);
    }

    // ── La orden guardada ───────────────────────────────────────────────────

    /// <summary>
    /// Una orden escrita antes de guardar al comprador se sigue leyendo, y avisa al primer
    /// asistente, que es lo que hacía.
    /// </summary>
    /// <remarks>
    /// El JSON es el que el registro escribía, con su forma de disco: sin <c>BuyerName</c> ni
    /// <c>BuyerEmail</c> (y sin <c>BuyerKind</c>/<c>BuyerId</c>, que llegaron en la F3). Una compra
    /// pendiente de antes de este cambio tiene que poder confirmarse y avisar.
    /// </remarks>
    [Fact]
    public async Task Una_orden_guardada_sin_comprador_se_lee_y_avisa_al_primer_asistente()
    {
        var store = new InMemoryJsonEntityStore();
        await store.WriteAsync(EventTicketLedger.ResourceType, "evord_anterior", """
            {
              "OrderRef": "evord_anterior",
              "EventId": "evt-festival-estereo",
              "PaymentSessionId": "stub_anterior",
              "Total": 360000,
              "Currency": "COP",
              "Units": [
                {
                  "TierCode": "GEN", "TierName": "General", "Seat": null, "Price": 180000, "Currency": "COP",
                  "AttendeeName": "Ana Asistente", "AttendeeEmail": "ana@ejemplo.co", "AttendeeDocument": null,
                  "ReservationId": "resv_0a1b2c3d4e5f60718293a4b5c6d7e8f9", "CheckedIn": false,
                  "HolderEmail": "ana@ejemplo.co", "HolderName": "Ana Asistente", "QrVersion": 0
                },
                {
                  "TierCode": "GEN", "TierName": "General", "Seat": null, "Price": 180000, "Currency": "COP",
                  "AttendeeName": "Beto Asistente", "AttendeeEmail": "beto@ejemplo.co", "AttendeeDocument": null,
                  "ReservationId": "resv_1a2b3c4d5e6f708192a3b4c5d6e7f809", "CheckedIn": false,
                  "HolderEmail": "beto@ejemplo.co", "HolderName": "Beto Asistente", "QrVersion": 0
                }
              ],
              "CreatedAt": "2026-07-01T12:00:00-05:00",
              "Status": 1
            }
            """);

        var orden = await new EventTicketLedger(store, Firmante).LoadAsync("evord_anterior");

        Assert.NotNull(orden);
        Assert.Null(orden!.BuyerEmail);
        var aviso = EventPurchaseNotification.Build(orden, Hoy);
        Assert.Equal("ana@ejemplo.co", aviso!.ToEmail);
        Assert.Equal("Ana Asistente", aviso.ToName);
    }

    /// <summary>
    /// empty: sin comprador guardado y sin asistentes no hay a quién avisar, y no se inventa.
    /// </summary>
    [Fact]
    public async Task Sin_comprador_ni_asistentes_no_se_avisa()
    {
        var buzon = new Buzon();
        var orden = new PersistedEventOrder(
            "evord_vacia", "evt-1", "ps_vacia", 0m, "COP", Array.Empty<PersistedEventUnit>(), Hoy);

        await EventPurchaseNotification.EmitAsync(buzon, orden, Hoy);

        Assert.Empty(buzon.Recibidos);
    }

    /// <summary>
    /// filter: un comprador guardado sin nombre no toma el del primer asistente.
    /// </summary>
    /// <remarks>
    /// Se cae a la primera unidad sólo cuando NO hay correo de comprador. Mezclar el correo de uno
    /// con el nombre de otro es mandarle a quien pagó un saludo ajeno; sin destinatario completo
    /// no se emite, como con cualquier destinatario inservible.
    /// </remarks>
    [Fact]
    public void Un_comprador_guardado_sin_nombre_no_toma_el_del_primer_asistente()
    {
        var orden = new PersistedEventOrder(
            "evord_mixta", "evt-1", "ps_mixta", 180_000m, "COP",
            new[]
            {
                new PersistedEventUnit("GEN", "General", null, 180_000m, "COP",
                    "Ana Asistente", "ana@ejemplo.co", null, "resv_0a1b2c3d4e5f60718293a4b5c6d7e8f9"),
            },
            Hoy)
        {
            BuyerName = " ",
            BuyerEmail = "regala@ejemplo.co",
        };

        Assert.Null(EventPurchaseNotification.Build(orden, Hoy));
    }
}
