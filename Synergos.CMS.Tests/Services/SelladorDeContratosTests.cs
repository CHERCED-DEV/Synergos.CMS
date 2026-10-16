using System.Text;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Quién sella y quién comprueba un contrato de alquiler (research R11 de la línea 005, T021): sella el del
/// interruptor, comprueba el que el contrato registra en <c>SelladoPor</c>.
/// </summary>
/// <remarks>
/// El sellador de <c>Api.Signing</c> es un doble con su propia regla de sello: un sello local nunca le cuadra, que es
/// justo lo que pasa con el de verdad (distinta llave, distinto algoritmo). Así, comprobar un contrato de antes con el
/// del interruptor se ve rojo.
/// </remarks>
public sealed class SelladorDeContratosTests
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

    private sealed class RelojFijo : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
    }

    /// <summary>El de <c>Api.Signing</c>, en memoria: sella con su regla y puede estar caído.</summary>
    private sealed class SelladorDeLaApiFalso : ISelladorDeContratos
    {
        public bool Caido { get; set; }

        public string Nombre => "api";

        public Task<SelloDelContrato> SellarAsync(AgreementSubject subject, CancellationToken cancellationToken = default)
            => Task.FromResult(Caido
                ? new SelloDelContrato(string.Empty, Nombre, Pendiente: true)
                : new SelloDelContrato(SelloDe(subject), "api:k-1"));

        public Task<bool?> ComprobarAsync(
            string? sello, AgreementSubject subject, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(Caido ? null : string.Equals(sello, SelloDe(subject), StringComparison.Ordinal));

        public Task<SelloDelContrato> SellarAsync(AgreementOutcomeSubject outcome, CancellationToken cancellationToken = default)
            => Task.FromResult(Caido
                ? new SelloDelContrato(string.Empty, Nombre, Pendiente: true)
                : new SelloDelContrato(SelloDe(outcome), "api:k-1"));

        public Task<bool?> ComprobarAsync(
            string? sello, AgreementOutcomeSubject outcome, CancellationToken cancellationToken = default)
            => Task.FromResult<bool?>(Caido ? null : string.Equals(sello, SelloDe(outcome), StringComparison.Ordinal));

        private static string SelloDe(AgreementSubject s) => $"api|{s.RentalId}|{s.RenterId}|{s.DepositHeld}|{s.Currency}";

        private static string SelloDe(AgreementOutcomeSubject s)
            => $"api-desenlace|{s.RentalId}|{s.Damage}|{s.DepositReleased}|{s.StaffRef}|{s.ConfirmedUtc:O}";
    }

    private static readonly byte[] Llave = Encoding.UTF8.GetBytes("llave-de-prueba-del-contrato-32b!");

    private static SelladorLocal Local() => new(new HmacAgreementSigner(Llave));

    private static Rental Alquiler(string id = "alq-1") => new(
        id, "andamio", 1, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9), "u-1",
        RentalState.Reserved, new RentalQuote("andamio", 1, 7, 30_000m, 210_000m, 250_000m, "COP"),
        250_000m, 0m);

    [Fact]
    public async Task Un_contrato_de_hoy_comprueba_igual_con_el_interruptor_en_Api()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var deHoy = await new EquipmentAgreementIssuer(ledger, Local(), new RelojFijo())
            .IssueAsync(Alquiler(), "Andamio");
        Assert.Equal(RentalAgreement.SelladoLocal, deHoy.SelladoPor);

        // Se conmuta a Api: el local ya no sella, pero comprueba lo que selló.
        var conmutado = new EquipmentAgreementIssuer(ledger, new SelladorDeLaApiFalso(), new RelojFijo(), [Local()]);

        Assert.True(await conmutado.ComprobarAsync(deHoy));
        // Y uno guardado antes del interruptor, sin quién lo selló, también es del local.
        Assert.True(await conmutado.ComprobarAsync(deHoy with { SelladoPor = string.Empty }));
    }

    [Fact]
    public async Task Un_sello_alterado_da_false_con_los_dos()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var api = new SelladorDeLaApiFalso();
        var local = await new EquipmentAgreementIssuer(ledger, Local(), new RelojFijo()).IssueAsync(Alquiler("alq-1"), "Andamio");
        var deLaApi = await new EquipmentAgreementIssuer(ledger, api, new RelojFijo(), [Local()]).IssueAsync(Alquiler("alq-2"), "Andamio");
        var emisor = new EquipmentAgreementIssuer(ledger, api, new RelojFijo(), [Local()]);

        Assert.False(await emisor.ComprobarAsync(local with { DepositHeld = 1m }));
        Assert.False(await emisor.ComprobarAsync(deLaApi with { DepositHeld = 1m }));
        Assert.False(await emisor.ComprobarAsync(local with { Seal = (local.Seal[0] == '0' ? "1" : "0") + local.Seal[1..] }));
    }

    [Fact]
    public async Task Sella_el_del_interruptor_y_el_contrato_registra_quien()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var emisor = new EquipmentAgreementIssuer(ledger, new SelladorDeLaApiFalso(), new RelojFijo(), [Local()]);

        var contrato = await emisor.IssueAsync(Alquiler(), "Andamio");

        Assert.Equal("api:k-1", contrato.SelladoPor);
        Assert.False(contrato.SelloPendiente);
        Assert.True(await emisor.ComprobarAsync(contrato));
        Assert.Equal("api:k-1", (await ledger.GetAsync("alq-1"))!.SelladoPor);
    }

    [Fact]
    public async Task Con_el_servicio_caido_queda_pendiente_y_no_se_da_por_falso()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var emisor = new EquipmentAgreementIssuer(
            ledger, new SelladorDeLaApiFalso { Caido = true }, new RelojFijo(), [Local()]);

        var contrato = await emisor.IssueAsync(Alquiler(), "Andamio");

        Assert.True(contrato.SelloPendiente);
        Assert.Equal(string.Empty, contrato.Seal);
        // Nunca se sella con la llave local en su lugar.
        Assert.Equal("api", contrato.SelladoPor);
        Assert.Null(await emisor.ComprobarAsync(contrato));
    }

    [Fact]
    public async Task Sin_quien_lo_sello_en_el_despliegue_no_se_pudo_comprobar_y_sin_sello_no_cuadra()
    {
        var ledger = new EquipmentAgreementLedger(new AlmacenEnMemoria());
        var deLaApi = await new EquipmentAgreementIssuer(ledger, new SelladorDeLaApiFalso(), new RelojFijo())
            .IssueAsync(Alquiler(), "Andamio");

        // De vuelta a Local sin Api.Signing configurado: nadie aquí puede comprobarlo, y eso no es «no cuadra».
        var deVuelta = new EquipmentAgreementIssuer(ledger, Local(), new RelojFijo());

        Assert.Null(await deVuelta.ComprobarAsync(deLaApi));
        Assert.False(await deVuelta.ComprobarAsync(deLaApi with { SelladoPor = RentalAgreement.SelladoLocal, Seal = "" }));
        Assert.False(await deVuelta.ComprobarAsync(null));
    }
}
