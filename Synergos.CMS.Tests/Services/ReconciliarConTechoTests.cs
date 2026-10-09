using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
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
/// La reconciliación de «mis entradas» y de la consola corre en la ruta de lectura, y por eso tiene techo
/// (ADR 0140 F3): lo que no se completará deja de mirarse, mira pocas a la vez, y un orquestador colgado no
/// deja la lista sin llegar.
/// </summary>
public sealed class ReconciliarConTechoTests
{
    private static readonly Guid Ana = Guid.Parse("8c0b1a52-7f1d-4c5e-9d6e-3a2b1c0d9e8f");
    private static readonly string SujetoDeAna = $"eventos.comprador:{Ana:n}";

    private readonly EventTicketLedger _ledger = new(signer: new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-del-techo")));

    /// <summary>Un orquestador que contesta por saga un estado, un 404 o nada (colgado), y cuenta.</summary>
    private sealed class Orquestador : HttpMessageHandler
    {
        private int _llamadas;

        public Dictionary<string, string?> Estados { get; } = new(StringComparer.Ordinal);

        public bool Colgado { get; set; }

        public int Llamadas => Volatile.Read(ref _llamadas);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Interlocked.Increment(ref _llamadas);
            if (Colgado) await Task.Delay(Timeout.Infinite, ct);

            var id = Uri.UnescapeDataString(r.RequestUri!.AbsolutePath.Split('/').Last());
            if (!Estados.TryGetValue(id, out var estado) || estado is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent("""{"code":"eventos.purchase_not_found","detail":"no","transient":false}""",
                        Encoding.UTF8, "application/problem+json"),
                };
            }

            var json = JsonSerializer.Serialize(new
            {
                id,
                buyerKind = "eventos.comprador",
                buyerId = $"{Ana:n}",
                eventId = "evt-1",
                status = estado,
                total = new { amount = 100_000m, currency = "COP" },
                held = new[] { new { tier = "GEN", seat = (string?)null, quantity = 1 } },
                pendingCompensations = 0,
            });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-eventos.local/") };
    }

    private ArtefactoDeEventos Artefacto(Orquestador orquestador, TimeSpan? presupuesto = null)
    {
        var puerta = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        puerta.CurrentValue.Returns(new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal)
            {
                [ArtefactoDeEventos.Flujo] = new() { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "eventos.comprador" },
            },
        });
        return new ArtefactoDeEventos(new Fabrica(orquestador), _ledger, puerta, NullLogger<ArtefactoDeEventos>.Instance,
            hayDestino: true, presupuesto: presupuesto);
    }

    private async Task<PersistedEventOrder> Pendiente(string saga, int minutosAtras = 0, string? buyerId = null, string correo = "ana@ejemplo.co")
    {
        var orden = new PersistedEventOrder("evord_" + saga, "evt-1", saga, 100_000m, "COP",
            [new PersistedEventUnit("GEN", "GEN", null, 0m, "COP", "Ana", "ana@ejemplo.co", null, $"{saga.Replace("-", "")}00")],
            DateTimeOffset.UtcNow.AddMinutes(-minutosAtras))
        { BuyerKind = "eventos.comprador", BuyerId = buyerId ?? $"{Ana:n}", BuyerEmail = correo };
        await _ledger.SaveAsync(orden);
        return orden;
    }

    private async Task<EventOrderStatus> Estado(string saga) => (await _ledger.LoadAsync("evord_" + saga))!.Status;

    [Fact]
    public async Task Lo_que_no_se_completara_se_descarta_y_no_se_vuelve_a_consultar_y_lo_que_corre_si()
    {
        using var orquestador = new Orquestador();
        orquestador.Estados["s-deshecha"] = "Compensated";
        orquestador.Estados["s-colgada"] = "CompensationFailed";
        orquestador.Estados["s-corre"] = "Running";
        foreach (var s in new[] { "s-deshecha", "s-colgada", "s-inexistente", "s-corre" }) await Pendiente(s);
        var artefacto = Artefacto(orquestador);

        await artefacto.ReconciliarAsync(_ => true, default);
        Assert.Equal(4, orquestador.Llamadas);
        Assert.Equal(EventOrderStatus.Discarded, await Estado("s-deshecha"));
        Assert.Equal(EventOrderStatus.Discarded, await Estado("s-colgada"));
        Assert.Equal(EventOrderStatus.Discarded, await Estado("s-inexistente"));
        Assert.Equal(EventOrderStatus.Pending, await Estado("s-corre"));

        await artefacto.ReconciliarAsync(_ => true, default);
        Assert.Equal(5, orquestador.Llamadas);   // sólo la que sigue en curso

        orquestador.Estados["s-corre"] = "Completed";
        await artefacto.ReconciliarAsync(_ => true, default);
        Assert.Equal(EventOrderStatus.Confirmed, await Estado("s-corre"));
    }

    [Fact]
    public async Task Mira_como_mucho_el_tope_las_mas_recientes_primero()
    {
        using var orquestador = new Orquestador();
        for (var i = 0; i < ArtefactoDeEventos.TopeDeReconciliacion + 5; i++)
        {
            orquestador.Estados[$"s-{i:D2}"] = i == 0 ? "Completed" : "Running";
            await Pendiente($"s-{i:D2}", minutosAtras: i);
        }

        await Artefacto(orquestador).ReconciliarAsync(_ => true, default);

        Assert.Equal(ArtefactoDeEventos.TopeDeReconciliacion, orquestador.Llamadas);
        Assert.Equal(EventOrderStatus.Confirmed, await Estado("s-00"));   // la más reciente entró
    }

    [Fact]
    public async Task Con_el_orquestador_colgado_mis_entradas_llega_igual_con_lo_ya_confirmado()
    {
        using var orquestador = new Orquestador { Colgado = true };
        await _ledger.SaveAsync((await Pendiente("s-ya")) with { Status = EventOrderStatus.Confirmed });
        for (var i = 0; i < 3; i++) await Pendiente($"s-abandonada-{i}");
        var reloj = Stopwatch.StartNew();

        var r = await Controlador(Artefacto(orquestador, TimeSpan.FromMilliseconds(300))).Tickets(default).WaitAsync(TimeSpan.FromSeconds(20));

        reloj.Stop();
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(5), $"tardó {reloj.Elapsed}");
        var mias = Assert.IsType<EventosController.TicketsResponse>(Assert.IsType<OkObjectResult>(r).Value);
        Assert.Single(mias.Tickets);
        Assert.Equal(EventOrderStatus.Pending, await Estado("s-abandonada-0"));   // colgado no es «no existe»
    }

    [Fact]
    public async Task Mis_entradas_no_reconcilia_las_ordenes_de_otros_aunque_lleven_su_correo()
    {
        // Un checkout anónimo puede poner el correo de cualquiera: esas órdenes no son del miembro.
        using var orquestador = new Orquestador();
        for (var i = 0; i < 3; i++) await Pendiente($"s-ajena-{i}", buyerId: "seudonimo-de-otro");

        Assert.IsType<OkObjectResult>(await Controlador(Artefacto(orquestador)).Tickets(default));

        Assert.Equal(0, orquestador.Llamadas);
    }

    private EventosController Controlador(ArtefactoDeEventos artefacto)
    {
        var gate = Substitute.For<IMemberAccessGate>();
        gate.IsAuthenticated.Returns(true);
        gate.CurrentMemberKey.Returns(Ana);
        gate.CurrentMemberEmail.Returns("ana@ejemplo.co");
        var ticketing = Substitute.For<IEventTicketingService>();
        ticketing.GetTicketsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => _ledger.TicketsOfAsync(ci.ArgAt<string>(0), default));

        return new EventosController(
            Substitute.For<IEventCatalogProvider>(), ticketing, Substitute.For<IEventManagementService>(),
            Substitute.For<IPriceFormatter>(), gate, Substitute.For<IRealtimeNotifier>(),
            NullLogger<EventosController>.Instance, artefacto: artefacto)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }
}
