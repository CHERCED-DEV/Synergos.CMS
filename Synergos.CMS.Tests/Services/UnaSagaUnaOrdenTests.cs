using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Tests.Controllers;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Una saga, una orden (ADR 0140 F3): anotar y confirmar la orden de una compra del orquestador es
/// atómico en el registro, sea cual sea el camino —la ruta vieja, el artefacto de la puerta o la
/// reconciliación de «mis entradas»—.
/// </summary>
/// <remarks>
/// <para><b>Las peticiones van a la vez de verdad.</b> El almacén de estos tests detiene cada lectura de
/// la lista hasta que llega la otra petición (o pasa un momento): sin exclusión en el registro, las dos
/// leen «no hay orden» antes de que ninguna escriba, que es la ventana que midió la verificación de la
/// F3. Con exclusión, la segunda no llega a leer hasta que la primera escribió.</para>
/// </remarks>
public sealed class UnaSagaUnaOrdenTests
{
    private static readonly ITicketSigner Firmante =
        new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-de-una-saga-una-orden"));

    private static readonly Guid Ana = Guid.Parse("8c0b1a52-7f1d-4c5e-9d6e-3a2b1c0d9e8f");

    /// <summary>Un almacén en memoria que hace coincidir a quien lista con la siguiente petición que liste.</summary>
    private sealed class AlmacenQueJunta : IJsonEntityStore
    {
        private readonly InMemoryJsonEntityStore _dentro = new();
        private readonly object _gate = new();
        private TaskCompletionSource _ronda = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _llegados;

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
            => _dentro.WriteAsync(resourceType, key, json, cancellationToken);

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => _dentro.ReadAsync(resourceType, key, cancellationToken);

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => _dentro.DeleteAsync(resourceType, key, cancellationToken);

        public async Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
        {
            Task ronda;
            lock (_gate)
            {
                ronda = _ronda.Task;
                if (++_llegados == 2)
                {
                    _ronda.TrySetResult();
                    _ronda = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _llegados = 0;
                }
            }

            // Sola, espera un momento a la otra; acompañada, siguen las dos juntas.
            await Task.WhenAny(ronda, Task.Delay(300, cancellationToken));
            lock (_gate)
            {
                if (!ronda.IsCompleted && _llegados > 0) _llegados--;
            }
            return await _dentro.ListAsync(resourceType, cancellationToken);
        }
    }

    private static PersistedEventOrder Orden(string orderRef, string saga, DateTimeOffset creada, string portador = "ana@ejemplo.co")
        => new(orderRef, "evt-1", saga, 200_000m, "COP",
            [new PersistedEventUnit("GEN", "GEN", null, 0m, "COP", "Ana", portador, null, $"{saga.Replace("-", "")}00")],
            creada)
        { BuyerKind = "eventos.comprador", BuyerId = $"{Ana:n}", BuyerEmail = "ana@ejemplo.co" };

    // ── El registro ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Dos_ordenes_gemelas_confirmadas_a_la_vez_confirman_UNA()
    {
        var registro = new EventTicketLedger(new AlmacenQueJunta(), Firmante);
        var a = Orden("evord_a", "saga-gemelas", DateTimeOffset.UnixEpoch);
        var b = Orden("evord_b", "saga-gemelas", DateTimeOffset.UnixEpoch.AddSeconds(1), "beto@ejemplo.co");
        await registro.SaveAsync(a);
        await registro.SaveAsync(b);

        var hechas = await Task.WhenAll(
            Task.Run(() => registro.ConfirmarAsync("saga-gemelas", _ => a)),
            Task.Run(() => registro.ConfirmarAsync("saga-gemelas", _ => b)));

        Assert.Single(await registro.LoadAllAsync(), o => o.Status == EventOrderStatus.Confirmed);
        Assert.Equal(hechas[0].OrderRef, hechas[1].OrderRef);   // las dos ven la MISMA confirmada
    }

    [Fact]
    public async Task Una_orden_confirmada_no_vuelve_a_pendiente_y_una_saga_no_anota_una_segunda()
    {
        var registro = new EventTicketLedger(signer: Firmante);
        var a = Orden("evord_a", "saga-una", DateTimeOffset.UnixEpoch);
        Assert.NotNull(await registro.AnotarPendienteAsync("saga-una", _ => a));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            registro.AnotarPendienteAsync("saga-una", _ => Orden("evord_b", "saga-una", DateTimeOffset.UnixEpoch)));

        await registro.ConfirmarAsync("saga-una", actual => actual!);
        Assert.Null(await registro.AnotarPendienteAsync("saga-una", actual => actual));
        Assert.Equal(EventOrderStatus.Confirmed, Assert.Single(await registro.LoadAllAsync()).Status);
    }

    // ── El artefacto de la puerta ───────────────────────────────────────────

    private const string Compra = "pta-1111111111111111111111111111111111111111";

    private static ArtefactoDeEventos Artefacto(EventTicketLedger registro, EventosArtefactoTests.Orquestador orquestador)
    {
        var puerta = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        puerta.CurrentValue.Returns(new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal)
            {
                [ArtefactoDeEventos.Flujo] = new() { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "eventos.comprador" },
            },
        });
        return new ArtefactoDeEventos(new Fabrica(orquestador), registro, puerta, NullLogger<ArtefactoDeEventos>.Instance, hayDestino: true);
    }

    private static EventosArtefactoTests.Orquestador ConLaCompra(string estado)
    {
        var orquestador = new EventosArtefactoTests.Orquestador();
        orquestador.Sagas[Compra] = new EventosArtefactoTests.Saga($"eventos.comprador:{Ana:n}", estado, [("GEN", null, 2)]);
        return orquestador;
    }

    [Fact]
    public async Task Dos_peticiones_de_las_entradas_a_la_vez_emiten_UNA_orden()
    {
        var registro = new EventTicketLedger(new AlmacenQueJunta(), Firmante);
        using var orquestador = ConLaCompra("Completed");
        var artefacto = Artefacto(registro, orquestador);

        var dos = await Task.WhenAll(
            Task.Run(() => artefacto.EntradasAsync(Compra, Ana, "ana@ejemplo.co", "Ana María", default)),
            Task.Run(() => artefacto.EntradasAsync(Compra, Ana, "ana@ejemplo.co", "Ana María", default)));

        var orden = Assert.Single(await registro.LoadAllAsync());
        Assert.Equal(EventOrderStatus.Confirmed, orden.Status);
        Assert.Equal(2, (await registro.ConfirmedAttendeesAsync("evt-1")).Count);
        Assert.All(dos, r => Assert.Equal(2, r.Entradas!.Tickets.Count));
        Assert.Equal(dos[0].Entradas!.Tickets.Select(t => t.Qr), dos[1].Entradas!.Tickets.Select(t => t.Qr));
    }

    [Fact]
    public async Task Dos_anotaciones_de_asistentes_a_la_vez_dejan_UNA_orden_con_la_ultima_lista()
    {
        var registro = new EventTicketLedger(new AlmacenQueJunta(), Firmante);
        using var orquestador = ConLaCompra("Running");
        var artefacto = Artefacto(registro, orquestador);
        EventAttendeeInfo[] Lista(string a, string b) =>
            [new(a, $"{a.ToLowerInvariant()}@ejemplo.co", null), new(b, $"{b.ToLowerInvariant()}@ejemplo.co", null)];

        var dos = await Task.WhenAll(
            Task.Run(() => artefacto.AnotarAsistentesAsync(Compra, Ana, "ana@ejemplo.co", "Ana", Lista("Beto", "Caro"), default)),
            Task.Run(() => artefacto.AnotarAsistentesAsync(Compra, Ana, "ana@ejemplo.co", "Ana", Lista("Dani", "Eva"), default)));

        Assert.All(dos, r => Assert.True(r.Bien));
        var orden = Assert.Single(await registro.LoadAllAsync());
        Assert.Equal(EventOrderStatus.Pending, orden.Status);

        orquestador.Sagas[Compra] = orquestador.Sagas[Compra] with { Estado = "Completed" };
        var entradas = await artefacto.EntradasAsync(Compra, Ana, "ana@ejemplo.co", "Ana", default);
        Assert.Equal(orden.Units.Select(u => u.AttendeeEmail), entradas.Entradas!.Tickets.Select(t => t.HolderEmail));
        Assert.Equal(2, (await registro.ConfirmedAttendeesAsync("evt-1")).Count);
    }

    [Fact]
    public async Task Reconciliar_no_confirma_la_gemela_de_una_saga_ya_confirmada()
    {
        // Dos órdenes de la misma saga, anotadas antes de que el registro anotara una sola: la de Ana ya
        // emitió. «Mis entradas» de quien tenga la otra no puede emitir las mismas butacas otra vez.
        var registro = new EventTicketLedger(signer: Firmante);
        using var orquestador = ConLaCompra("Completed");
        await registro.SaveAsync(Orden("evord_a", Compra, DateTimeOffset.UnixEpoch) with { Status = EventOrderStatus.Confirmed });
        await registro.SaveAsync(Orden("evord_b", Compra, DateTimeOffset.UnixEpoch.AddSeconds(1), "intruso@ejemplo.co"));

        await Artefacto(registro, orquestador).ReconciliarAsync(_ => true, default);

        Assert.Equal(EventOrderStatus.Discarded, (await registro.LoadAsync("evord_b"))!.Status);   // y no se vuelve a mirar
        Assert.Equal(["ana@ejemplo.co"], (await registro.ConfirmedAttendeesAsync("evt-1")).Select(a => a.Email));
    }

    // ── La ruta vieja ───────────────────────────────────────────────────────

    /// <summary>Un orquestador que se porta como el de verdad con la llave: la misma llave, la misma saga.</summary>
    private sealed class OrquestadorPorLlave : HttpMessageHandler
    {
        private readonly ConcurrentDictionary<string, JsonObject> _sagas = new(StringComparer.Ordinal);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            if (req.Method != HttpMethod.Post || req.RequestUri!.AbsolutePath != "/v1/ticket-purchases")
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var llave = req.Headers.GetValues("Idempotency-Key").Single();
            var sujeto = req.Headers.GetValues("X-Synergos-Sujeto").Single().Split(':', 2);
            var lineas = JsonNode.Parse(await req.Content!.ReadAsStringAsync(ct))!["lines"]!.ToJsonString();
            var saga = _sagas.GetOrAdd(llave, id => new JsonObject
            {
                ["id"] = id,
                ["buyerKind"] = sujeto[0],
                ["buyerId"] = sujeto[1],
                ["eventId"] = "evt-1",
                ["status"] = "Running",
                ["total"] = new JsonObject { ["amount"] = 240000, ["currency"] = "COP" },
                ["held"] = JsonNode.Parse(lineas),
                ["pendingCompensations"] = 0,
            });
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(saga.ToJsonString(), Encoding.UTF8, "application/json"),
            };
        }
    }

    [Fact]
    public async Task Dos_checkouts_a_la_vez_con_los_mismos_datos_anotan_UNA_orden()
    {
        var registro = new EventTicketLedger(new AlmacenQueJunta(), Firmante);
        using var orquestador = new OrquestadorPorLlave();
        var svc = new HttpEventTicketingService(
            new Fabrica(orquestador),
            new Monitor<EventosSettings>(new EventosSettings { Mode = "Bff" }),
            registro,
            HttpEventTicketingServiceTests.Cartelera(),
            NullLogger<HttpEventTicketingService>.Instance,
            now: () => HttpEventTicketingServiceTests.Hoy);
        EventAttendeeInfo[] Asistentes(string otro) =>
            [new("Ana Compradora", "ana@ejemplo.co", "1001"), new(otro, $"{otro.ToLowerInvariant()}@ejemplo.co", "1002")];
        Task<EventCheckoutResult> Comprar(string otro)
            => Task.Run(() => svc.CheckoutAsync("evt-1", [new EventCheckoutItem("GEN", null, 2)], Asistentes(otro)));

        var dos = new[] { Comprar("Beto"), Comprar("Zoe") };
        try { await Task.WhenAll(dos); } catch (ArgumentException) { /* uno de los dos, mirado abajo */ }

        Assert.Equal(1, dos.Count(t => t.IsCompletedSuccessfully));
        Assert.IsType<ArgumentException>(Assert.Single(dos, t => t.IsFaulted).Exception!.InnerException);
        Assert.Single(await registro.LoadAllAsync());
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-eventos.local/") };
    }

    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
