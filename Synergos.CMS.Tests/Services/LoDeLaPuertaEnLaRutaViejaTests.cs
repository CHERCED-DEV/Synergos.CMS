using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Lo que la F3 cierra alrededor del artefacto de la puerta (ADR 0140): la ruta vieja ANÓNIMA no confirma
/// ni entrega una compra hecha por la puerta, y el nombre del miembro nunca es su correo.
/// </summary>
public sealed class LoDeLaPuertaEnLaRutaViejaTests
{
    private static readonly ITicketSigner Firmante = new HmacTicketSigner(Encoding.UTF8.GetBytes("llave-de-tests-de-la-ruta-vieja"));

    private static PersistedEventOrder Orden(string orderRef, EventOrderStatus estado, bool porLaPuerta)
        => new(orderRef, "evt-festival-estereo", "pta-" + orderRef, 100_000m, "COP",
            [new PersistedEventUnit("GEN", "General", null, 0m, "COP", "Ana María", "ana@ejemplo.co", null, orderRef.Replace("_", "") + "00")],
            DateTimeOffset.UnixEpoch)
        { Status = estado, ViaGate = porLaPuerta, BuyerEmail = "ana@ejemplo.co" };

    [Theory]
    [InlineData(EventOrderStatus.Confirmed)]
    [InlineData(EventOrderStatus.Pending)]
    public async Task El_motor_en_proceso_no_confirma_ni_avisa_una_compra_de_la_puerta(EventOrderStatus estado)
    {
        var registro = new EventTicketLedger(signer: Firmante);
        var avisos = Substitute.For<ITransactionalNotifier>();
        var motor = new StubEventTicketingService(
            new StubEventCatalogProvider(), new StubReservationService(), new StubPaymentProvider(),
            tracking: null, audit: null, store: null, now: null, notifier: avisos, signer: Firmante, ledger: registro);
        await registro.SaveAsync(Orden("evord_puerta", estado, porLaPuerta: true));
        await registro.SaveAsync(Orden("evord_vieja", EventOrderStatus.Confirmed, porLaPuerta: false));

        await Assert.ThrowsAsync<ArgumentException>(() => motor.ConfirmAsync("evord_puerta"));
        Assert.Empty(avisos.ReceivedCalls());

        // El control: una orden de la ruta vieja sí se entrega por la ruta vieja.
        Assert.Single((await motor.ConfirmAsync("evord_vieja")).Tickets);
    }

    [Fact]
    public async Task La_ruta_vieja_hacia_el_orquestador_tampoco()
    {
        var registro = new EventTicketLedger(signer: Firmante);
        var red = Substitute.For<IHttpClientFactory>();
        var svc = new HttpEventTicketingService(
            red, new Monitor<EventosSettings>(new EventosSettings { Mode = "Bff" }), registro,
            HttpEventTicketingServiceTests.Cartelera(), NullLogger<HttpEventTicketingService>.Instance);
        await registro.SaveAsync(Orden("evord_puerta", EventOrderStatus.Confirmed, porLaPuerta: true));
        await registro.SaveAsync(Orden("evord_otra", EventOrderStatus.Pending, porLaPuerta: true));

        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmAsync("evord_puerta"));
        await Assert.ThrowsAsync<ArgumentException>(() => svc.ConfirmAsync("evord_otra"));
        Assert.Empty(red.ReceivedCalls());
    }

    // ── El nombre del miembro ───────────────────────────────────────────────

    private static string? NombreVisible(string login, string? nombreDelMiembro, bool autenticado = true)
    {
        var miembro = Substitute.For<IMember>();
        miembro.Name.Returns(nombreDelMiembro);
        var miembros = Substitute.For<IMemberService>();
        miembros.GetById(1234).Returns(miembro);

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, login), new Claim(ClaimTypes.NameIdentifier, "1234")],
                autenticado ? "Cookies" : null)),
            RequestServices = new ServiceCollection().AddSingleton(miembros).BuildServiceProvider(),
        };
        return new DefaultMemberAccessGate(new HttpContextAccessor { HttpContext = http }).CurrentMemberDisplayName;
    }

    [Fact]
    public void El_nombre_visible_es_el_del_miembro_y_nunca_su_correo()
    {
        Assert.Equal("Compradora F3 A", NombreVisible("comprador-a.f3@synergos.test", "Compradora F3 A"));
        Assert.Null(NombreVisible("comprador-a.f3@synergos.test", "comprador-a.f3@synergos.test"));
        Assert.Null(NombreVisible("comprador-a.f3@synergos.test", null));
        Assert.Equal("ana.maria", NombreVisible("ana.maria", null));   // un login que no es un correo, sí
        Assert.Null(NombreVisible("comprador-a.f3@synergos.test", "Compradora F3 A", autenticado: false));
    }

    [Fact]
    public async Task Sin_nombre_la_entrada_y_la_orden_de_la_puerta_no_llevan_el_correo_como_nombre()
    {
        var registro = new EventTicketLedger(signer: Firmante);
        using var orquestador = new Controllers.EventosArtefactoTests.Orquestador();
        var ana = Guid.Parse("8c0b1a52-7f1d-4c5e-9d6e-3a2b1c0d9e8f");
        orquestador.Sagas["pta-sin-nombre"] = new Controllers.EventosArtefactoTests.Saga($"eventos.comprador:{ana:n}", "Completed", [("GEN", null, 1)]);
        var puerta = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        puerta.CurrentValue.Returns(new PuertaSettings
        {
            Flujos = new Dictionary<string, FlujoDeLaPuertaSettings>(StringComparer.Ordinal)
            {
                [ArtefactoDeEventos.Flujo] = new() { Acceso = AccesoDeLaPuerta.Miembro, SujetoKind = "eventos.comprador" },
            },
        });
        var artefacto = new ArtefactoDeEventos(new Fabrica(orquestador), registro, puerta, NullLogger<ArtefactoDeEventos>.Instance, hayDestino: true);

        var r = await artefacto.EntradasAsync("pta-sin-nombre", ana, "ana@ejemplo.co", nombre: null, default);

        var entrada = Assert.Single(r.Entradas!.Tickets);
        Assert.Equal("ana@ejemplo.co", entrada.HolderEmail);
        Assert.Equal(string.Empty, entrada.AttendeeName);
        var orden = Assert.Single(await registro.LoadAllAsync());
        Assert.Null(orden.BuyerName);
        Assert.True(orden.ViaGate);
    }

    private sealed class Fabrica(HttpMessageHandler cable) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(cable, disposeHandler: false) { BaseAddress = new Uri("http://bff-eventos.local/") };
    }

    private sealed class Monitor<T>(T valor) : IOptionsMonitor<T>
    {
        public T CurrentValue => valor;
        public T Get(string? name) => valor;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
