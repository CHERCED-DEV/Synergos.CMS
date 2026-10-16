using System.Text;
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
/// El desenlace de la devolución, sellado aparte (lote 3, 005-P6; FR-023; T025): lo que confirmó el personal comprueba
/// por su lado, y el sello del contrato —lo pactado al salir— no cambia y sigue comprobando.
/// </summary>
public sealed class DesenlaceSelladoTests
{
    private sealed class AlmacenEnMemoria : IJsonEntityStore
    {
        private readonly Dictionary<string, string> _data = new(StringComparer.Ordinal);

        public Task WriteAsync(string resourceType, string key, string json, CancellationToken cancellationToken = default)
        {
            _data[resourceType + "/" + key] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.TryGetValue(resourceType + "/" + key, out var json) ? json : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value)
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_data.Remove(resourceType + "/" + key));
    }

    private sealed class RelojQueAvanza : TimeProvider
    {
        private DateTimeOffset _ahora = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _ahora = _ahora.AddMinutes(1);
    }

    /// <summary>El de <c>Api.Signing</c>, en memoria, que puede estar caído.</summary>
    private sealed class SelladorDeLaApiFalso : ISelladorDeContratos
    {
        public bool Caido { get; set; }

        public string Nombre => "api";

        public Task<SelloDelContrato> SellarAsync(AgreementSubject subject, CancellationToken cancellationToken = default)
            => Task.FromResult(new SelloDelContrato("api|" + subject.RentalId, "api:k-1"));

        public Task<bool?> ComprobarAsync(string? sello, AgreementSubject subject, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(sello == "api|" + subject.RentalId);

        public Task<SelloDelContrato> SellarAsync(AgreementOutcomeSubject outcome, CancellationToken cancellationToken = default)
            => Task.FromResult(Caido
                ? new SelloDelContrato(string.Empty, Nombre, Pendiente: true)
                : new SelloDelContrato(Sello(outcome), "api:k-1"));

        public Task<bool?> ComprobarAsync(string? sello, AgreementOutcomeSubject outcome, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(Caido ? null : sello == Sello(outcome));

        private static string Sello(AgreementOutcomeSubject s) => $"api-desenlace|{s.RentalId}|{s.Damage}|{s.StaffRef}|{s.ConfirmedUtc:O}";
    }

    private sealed class Sesion(Guid? miembro, bool personal) : IMemberAccessGate
    {
        public bool IsAuthenticated => miembro is not null;
        public string? CurrentMemberDisplayName => null;
        public string? CurrentMemberEmail => null;
        public Guid? CurrentMemberKey => miembro;
        public IReadOnlyCollection<string> CurrentMemberRoles => [];
        public bool HasAnyRole(string? allowedRolesCsv) => personal;
    }

    private static readonly Guid Ana = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Personal = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    private static SelladorLocal Local() => new(new HmacAgreementSigner(Encoding.UTF8.GetBytes("llave-local-del-cms-32-bytes-xx!")));

    private static Rental Alquiler(RentalState estado = RentalState.Reserved, decimal retenido = 250_000m, decimal danio = 0m) => new(
        "alq-1", "andamio", 1, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), Ana.ToString("N"),
        estado, new RentalQuote("andamio", 1, 7, 30_000m, 210_000m, 250_000m, "COP"), retenido, danio);

    [Fact]
    public async Task El_desenlace_comprueba_aparte_y_el_sello_del_contrato_no_cambia_y_comprueba()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var emisor = new EquipmentAgreementIssuer(ledger, Local(), new RelojQueAvanza());
        var contrato = await emisor.IssueAsync(Alquiler(), "Andamio");

        var cerrado = await emisor.SellarDesenlaceAsync(contrato, 40_000m, 210_000m, "personal-1");

        Assert.Equal(contrato.Seal, cerrado.Seal);
        Assert.True(await emisor.ComprobarAsync(cerrado));
        var d = Assert.IsType<DesenlaceDelAlquiler>(cerrado.Desenlace);
        Assert.Equal((40_000m, 210_000m, "personal-1"), (d.Danio, d.GarantiaLiberada, d.Personal));
        Assert.NotEqual(string.Empty, d.Sello);
        Assert.True(await emisor.ComprobarDesenlaceAsync(cerrado));
        Assert.Equal(cerrado, await ledger.GetAsync("alq-1"));

        // Lo que afirma el desenlace también se comprueba: otro daño, u otro personal, no cuadran.
        Assert.False(await emisor.ComprobarDesenlaceAsync(cerrado with { Desenlace = d with { Danio = 0m } }));
        Assert.False(await emisor.ComprobarDesenlaceAsync(cerrado with { Desenlace = d with { Personal = "otro" } }));
        Assert.False(await emisor.ComprobarDesenlaceAsync(contrato));
    }

    [Fact]
    public void Un_sello_de_desenlace_no_vale_como_contrato_ni_al_reves()
    {
        var firmante = new HmacAgreementSigner(Encoding.UTF8.GetBytes("llave-local-del-cms-32-bytes-xx!"));
        var contrato = new AgreementSubject("alq-1", "u-1", "andamio", 250_000m, "COP");
        var desenlace = new AgreementOutcomeSubject("alq-1", "u-1", "andamio", 0m, 250_000m, "COP", "p-1",
            new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.Zero));

        Assert.False(firmante.Matches(firmante.Seal(desenlace), contrato));
        Assert.False(firmante.Matches(firmante.Seal(contrato), desenlace));
        Assert.True(firmante.Matches(firmante.Seal(desenlace), desenlace));
    }

    [Fact]
    public async Task Con_la_firma_caida_el_desenlace_queda_pendiente_y_se_sella_al_leer()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var api = new SelladorDeLaApiFalso { Caido = true };
        var emisor = new EquipmentAgreementIssuer(ledger, api, new RelojQueAvanza(), [Local()]);
        var contrato = await emisor.IssueAsync(Alquiler(), "Andamio");

        var cerrado = await emisor.SellarDesenlaceAsync(contrato, 0m, 250_000m, "personal-1");
        Assert.True(cerrado.Desenlace!.SelloPendiente);
        Assert.Equal("api", cerrado.Desenlace.SelladoPor);
        Assert.Null((await emisor.LeerAsync(cerrado)).DesenlaceComprobado);

        api.Caido = false;
        var leido = await emisor.LeerAsync(cerrado);

        Assert.True(leido.DesenlaceComprobado);
        Assert.True(leido.Comprobado);
        Assert.False(leido.Contrato.Desenlace!.SelloPendiente);
        Assert.Equal(leido.Contrato.Desenlace, (await ledger.GetAsync("alq-1"))!.Desenlace);
    }

    [Fact]
    public async Task Confirmar_la_devolucion_en_el_borde_sella_el_desenlace_una_vez_y_el_contrato_sigue_igual()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var emisor = new EquipmentAgreementIssuer(ledger, Local(), new RelojQueAvanza());
        var motor = Substitute.For<IEquipmentRentalService>();
        motor.ReserveAsync(Arg.Any<RentalRequest>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RentalResult(RentalOutcome.Ok, Alquiler(), null, null));
        motor.ReturnAsync("alq-1", 40_000m, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new RentalResult(RentalOutcome.Ok, Alquiler(RentalState.Returned, 0m, 40_000m), null, null));
        var puerta = Substitute.For<IOptionsMonitor<PuertaSettings>>();
        puerta.CurrentValue.Returns(new PuertaSettings());
        var artefacto = new ArtefactoDeAlquiler(Substitute.For<IHttpClientFactory>(), ledger, emisor,
            new StubEquipmentCatalogProvider(), puerta, NullLogger<ArtefactoDeAlquiler>.Instance, hayDestino: false);
        AlquilerController Borde(Guid quien, bool personal) => new(new StubEquipmentCatalogProvider(), motor,
            Substitute.For<IAlquilerDeLaPuerta>(), emisor, ledger, artefacto, new Sesion(quien, personal),
            NullLogger<AlquilerController>.Instance);

        await Borde(Ana, false).Reserve(
            new ReserveRequest("andamio", 1, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), "reservar-1"),
            CancellationToken.None);
        var sello = (await ledger.GetAsync("alq-1"))!.Seal;

        var res = await Borde(Personal, true).Return("alq-1", new SettleRequest(40_000m, "devolver-1"), CancellationToken.None);

        var dto = Assert.IsType<RentalDto>(Assert.IsType<OkObjectResult>(res.Result).Value);
        var contrato = dto.Agreement!;
        Assert.Equal((sello, (bool?)true), (contrato.Seal, contrato.Verified));
        var desenlace = Assert.IsType<AgreementOutcomeDto>(contrato.Outcome);
        Assert.Equal((40_000m, (decimal?)210_000m, (bool?)true), (desenlace.Damage, desenlace.DepositReleased, desenlace.Verified));
        Assert.Equal(Personal.ToString("N"), (await ledger.GetAsync("alq-1"))!.Desenlace!.Personal);

        // Repetir la confirmación no vuelve a sellar: el desenlace es el de la primera vez.
        var otra = await Borde(Personal, true).Return("alq-1", new SettleRequest(40_000m, "devolver-1"), CancellationToken.None);
        var otraVez = ((RentalDto)((OkObjectResult)otra.Result!).Value!).Agreement!.Outcome!;
        Assert.Equal((desenlace.ConfirmedUtc, desenlace.Seal), (otraVez.ConfirmedUtc, otraVez.Seal));
    }
}
