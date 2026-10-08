using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// En la compra de Eventos contra el orquestador, la orden que recibe el navegador es la credencial
/// del invitado: sólo la tiene quien hizo el checkout, igual que en el motor en proceso.
/// </summary>
/// <remarks>
/// <para><b>La compra es anónima</b>, así que lo único que separa a quien compró de cualquier otro
/// es la <c>orderRef</c> que le devolvió SU checkout. <c>EventosControllerTests</c> lo dice del motor
/// en proceso: <c>evord_{guid}</c> es inadivinable y por eso funciona como credencial. En este camino
/// la orden ERA el id de la saga, y el id de la saga se deriva de lo que se compra —para que un
/// reintento no cobre dos veces—, o sea de datos que no son secretos.</para>
///
/// <para><b>El id de la saga sigue siendo determinista y se queda de este lado</b>; la orden que sale
/// al navegador es aleatoria. Y un checkout sólo entrega la orden que él mismo creó: si la saga ya
/// tiene una, se rechaza sin nombrarla y sin tocarla.</para>
///
/// <para>El orquestador de estos tests se porta como el de verdad en lo que importa acá: el id de la
/// saga es la llave de idempotencia y la misma llave devuelve la misma saga.</para>
/// </remarks>
public sealed class OrdenDelInvitadoEnEventosTests
{
    private sealed class OrquestadorConSagas : HttpMessageHandler
    {
        private const string Raiz = "/v1/ticket-purchases";
        private readonly Dictionary<string, JsonObject> _sagas = new(StringComparer.Ordinal);

        public int Sagas => _sagas.Count;

        public bool Existe(string id) => _sagas.ContainsKey(id);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            var path = req.RequestUri!.AbsolutePath;

            if (req.Method == HttpMethod.Post && path == Raiz)
            {
                var llave = req.Headers.GetValues("Idempotency-Key").Single();
                if (!_sagas.TryGetValue(llave, out var saga))
                {
                    var cuerpo = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!.AsObject();
                    // Quién compra llega en la cabecera de la puerta (ADR 0140 F3), como en el de verdad.
                    var sujeto = req.Headers.GetValues("X-Synergos-Sujeto").Single().Split(':', 2);
                    saga = new JsonObject
                    {
                        ["id"] = llave,
                        ["buyerKind"] = sujeto[0],
                        ["buyerId"] = sujeto[1],
                        ["eventId"] = cuerpo["eventId"]?.GetValue<string>(),
                        ["status"] = "Running",
                        ["total"] = new JsonObject { ["amount"] = 240000, ["currency"] = "COP" },
                        ["held"] = JsonNode.Parse(cuerpo["lines"]!.ToJsonString()),
                        ["pendingCompensations"] = 0,
                        ["lastError"] = null,
                    };
                    _sagas[llave] = saga;
                }
                return Responder(HttpStatusCode.Created, saga);
            }

            if (!path.StartsWith(Raiz + "/", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var resto = path[(Raiz.Length + 1)..];
            var confirmar = req.Method == HttpMethod.Post && resto.EndsWith("/confirm", StringComparison.Ordinal);
            var id = Uri.UnescapeDataString(confirmar ? resto[..^"/confirm".Length] : resto);
            if (!_sagas.TryGetValue(id, out var existente))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (confirmar)
            {
                existente["status"] = "Completed";
            }
            return Responder(HttpStatusCode.OK, existente);
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

    private static readonly ITicketSigner Firmante =
        new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-de-la-orden-del-invitado"));

    private static readonly IReadOnlyList<EventCheckoutItem> DosGenerales =
        new[] { new EventCheckoutItem("GEN", null, 2) };

    private static readonly IReadOnlyList<EventAttendeeInfo> Dos = new[]
    {
        new EventAttendeeInfo("Ana Compradora", "ana@ejemplo.co", "1001"),
        new EventAttendeeInfo("Beto Acompañante", "beto@ejemplo.co", "1002"),
    };

    /// <summary>Otros dos asistentes, con el MISMO comprador declarado en el formulario.</summary>
    private static readonly IReadOnlyList<EventAttendeeInfo> OtrosDos = new[]
    {
        new EventAttendeeInfo("Zoe Otra", "zoe@ejemplo.co", "2001"),
        new EventAttendeeInfo("Yago Otro", "yago@ejemplo.co", "2002"),
    };

    private static readonly EventBuyerInfo LaMismaCompradora = new("Ana Compradora", "ana@ejemplo.co");

    private static (HttpEventTicketingService Svc, EventTicketLedger Registro) Nuevo(OrquestadorConSagas orq)
    {
        var registro = new EventTicketLedger(signer: Firmante);
        return (new HttpEventTicketingService(
            new Fabrica(orq),
            new Monitor<EventosSettings>(new EventosSettings { Mode = "Bff" }),
            registro,
            HttpEventTicketingServiceTests.Cartelera(),
            NullLogger<HttpEventTicketingService>.Instance,
            now: () => HttpEventTicketingServiceTests.Hoy), registro);
    }

    /// <summary>La saga de la compra de Ana: lo único que hace falta para nombrarla es lo que compró.</summary>
    private static string SagaDeAna()
        => HttpEventTicketingService.IdempotencyKeyFor(
            "evt-1", HttpEventTicketingService.BuyerId(Dos[0]), DosGenerales);

    private static EventosController Controlador(IEventTicketingService ticketing)
    {
        var anonimo = Substitute.For<IMemberAccessGate>();
        anonimo.IsAuthenticated.Returns(false);
        return new EventosController(
            HttpEventTicketingServiceTests.Cartelera(),
            ticketing,
            Substitute.For<IEventManagementService>(),
            Substitute.For<IPriceFormatter>(),
            anonimo,
            Substitute.For<IRealtimeNotifier>(),
            NullLogger<EventosController>.Instance);
    }

    [Fact]
    public async Task La_orden_que_recibe_el_navegador_no_se_deduce_de_lo_que_se_compro()
    {
        var orq = new OrquestadorConSagas();
        var (svc, _) = Nuevo(orq);

        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        // Control: la saga SÍ es la llave derivada de la compra. Sin esto el test pasaría contra
        // un orquestador que inventa ids y no diría nada del de verdad.
        Assert.True(orq.Existe(SagaDeAna()));

        Assert.Matches("^evord_[0-9a-f]{32}$", compra.OrderRef);
        Assert.NotEqual(SagaDeAna(), compra.OrderRef);
        Assert.NotEqual(SagaDeAna(), compra.PaymentSessionId);

        await svc.ConfirmAsync(compra.OrderRef);
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmAsync(SagaDeAna()));
    }

    /// <summary>Por la ruta anónima, sólo la orden del checkout devuelve las entradas.</summary>
    [Fact]
    public async Task Por_la_ruta_anonima_solo_la_orden_del_checkout_devuelve_las_entradas()
    {
        var orq = new OrquestadorConSagas();
        var (svc, _) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);
        await svc.ConfirmAsync(compra.OrderRef);
        var controlador = Controlador(svc);

        Assert.IsType<NotFoundObjectResult>(
            await controlador.Confirm(new EventosController.ConfirmRequest(SagaDeAna()), default));

        // Control: con SU orden, la misma ruta sí le devuelve sus dos entradas.
        var ok = Assert.IsType<OkObjectResult>(
            await controlador.Confirm(new EventosController.ConfirmRequest(compra.OrderRef), default));
        var cuerpo = Assert.IsType<EventosController.ConfirmResponse>(ok.Value);
        Assert.Equal(2, cuerpo.Tickets.Count);
    }

    /// <summary>
    /// Un checkout con los mismos datos que una compra ya confirmada no la nombra ni la pisa.
    /// </summary>
    /// <remarks>
    /// Es la misma saga —la llave sale de los mismos datos— y eso está bien: es lo que impide
    /// cobrar dos veces. Lo que no puede pasar es que ese checkout devuelva la orden de la
    /// primera, ni que reescriba quién va a entrar.
    /// </remarks>
    [Fact]
    public async Task Un_checkout_con_los_mismos_datos_no_nombra_ni_pisa_una_compra_confirmada()
    {
        var orq = new OrquestadorConSagas();
        var (svc, registro) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);
        await svc.ConfirmAsync(compra.OrderRef);

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, OtrosDos, LaMismaCompradora));
        Assert.DoesNotContain(compra.OrderRef, ex.Message, StringComparison.Ordinal);

        Assert.Equal(1, orq.Sagas);
        var ordenes = await registro.LoadAllAsync();
        var orden = Assert.Single(ordenes);
        Assert.Equal(EventOrderStatus.Confirmed, orden.Status);
        Assert.Equal(new[] { "ana@ejemplo.co", "beto@ejemplo.co" }, orden.Units.Select(u => u.HolderEmail));
        Assert.Empty(await registro.TicketsOfAsync("zoe@ejemplo.co"));
    }

    /// <summary>…y tampoco se queda con una que todavía no se confirmó.</summary>
    [Fact]
    public async Task Un_checkout_con_los_mismos_datos_no_se_queda_con_una_compra_en_curso()
    {
        var orq = new OrquestadorConSagas();
        var (svc, registro) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);

        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.CheckoutAsync("evt-1", DosGenerales, OtrosDos, LaMismaCompradora));

        var confirmacion = await svc.ConfirmAsync(compra.OrderRef);
        Assert.Equal("Confirmed", confirmacion.Status);
        Assert.Equal(new[] { "ana@ejemplo.co", "beto@ejemplo.co" }, confirmacion.Tickets.Select(t => t.HolderEmail));
        Assert.Single(await registro.LoadAllAsync());
        Assert.Empty(await registro.TicketsOfAsync("zoe@ejemplo.co"));
    }

    /// <summary>
    /// Una saga emite sus entradas UNA vez, aunque dos órdenes la nombren.
    /// </summary>
    /// <remarks>
    /// Dos checkouts simultáneos de la misma compra pueden anotar dos órdenes antes de que ninguno
    /// vea la del otro. Confirmar la segunda emitiría las mismas butacas dos veces.
    /// </remarks>
    [Fact]
    public async Task Una_saga_emite_sus_entradas_una_sola_vez()
    {
        var orq = new OrquestadorConSagas();
        var (svc, registro) = Nuevo(orq);
        var compra = await svc.CheckoutAsync("evt-1", DosGenerales, Dos);
        var primera = (await registro.LoadAsync(compra.OrderRef))!;
        var gemela = primera with { OrderRef = "evord_" + new string('0', 32) };
        await registro.SaveAsync(gemela);

        await svc.ConfirmAsync(compra.OrderRef);

        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.ConfirmAsync(gemela.OrderRef));
        Assert.Equal(EventOrderStatus.Pending, (await registro.LoadAsync(gemela.OrderRef))!.Status);
        Assert.Equal(2, (await registro.TicketsOfAsync("ana@ejemplo.co")).Count
                        + (await registro.TicketsOfAsync("beto@ejemplo.co")).Count);
    }
}
