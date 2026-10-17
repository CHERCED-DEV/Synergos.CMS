using System.Text;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// El registro de contratos de Alquiler, con los campos que suma la renta por la puerta (línea 005, T006):
/// por dónde salió, quién lo selló, si el sello quedó pendiente, en qué punto está y cómo se cerró.
/// </summary>
/// <remarks>
/// <b>Los contratos ya guardados no se migran</b>: los campos nuevos van al final y con valor por defecto, así
/// que un contrato de antes se lee como lo que era —de la ruta directa, sellado con la llave local, reservado— y
/// su sello sigue comprobando. Lo prueba un JSON escrito a mano con la forma de HOY, no uno serializado por el
/// record nuevo, que traería los campos y no probaría nada.
/// </remarks>
public sealed class EquipmentAgreementLedgerTests
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
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    }

    private static readonly byte[] Llave = Encoding.UTF8.GetBytes("llave-de-prueba-del-contrato-32b!");

    [Fact]
    public async Task Un_contrato_de_antes_se_lee_como_era_y_su_sello_sigue_comprobando()
    {
        var firmante = new HmacAgreementSigner(Llave);
        var sello = firmante.Seal(new AgreementSubject("alq-1", "u-1", "andamio", 250_000m, "COP"));
        var almacen = new AlmacenEnMemoria();

        // La forma EXACTA que guarda hoy EquipmentAgreementLedger: doce campos y ninguno más.
        await almacen.WriteAsync(EquipmentAgreementLedger.ResourceType, "alq-1",
            "{\"RentalId\":\"alq-1\",\"RenterId\":\"u-1\",\"EquipmentId\":\"andamio\",\"EquipmentName\":\"Andamio\","
            + "\"Quantity\":1,\"Start\":\"2026-11-02\",\"End\":\"2026-11-09\",\"RentalTotal\":210000,"
            + "\"DepositHeld\":250000,\"Currency\":\"COP\",\"IssuedUtc\":\"2026-10-01T10:00:00+00:00\","
            + $"\"Seal\":\"{sello}\"}}");

        var leido = await new EquipmentAgreementLedger(almacen).GetAsync("alq-1");

        Assert.NotNull(leido);
        Assert.Equal(CaminoDeCompra.Directa, leido.Camino);
        Assert.Equal(RentalAgreement.SelladoLocal, leido.SelladoPor);
        Assert.False(leido.SelloPendiente);
        Assert.Equal(RentalState.Reserved, leido.Estado);
        Assert.Null(leido.Desenlace);

        var emisor = new EquipmentAgreementIssuer(new EquipmentAgreementLedger(almacen), new SelladorLocal(firmante), new RelojFijo());
        Assert.True(await emisor.ComprobarAsync(leido));
    }

    [Fact]
    public async Task Los_campos_nuevos_se_guardan_y_se_leen_de_vuelta()
    {
        var almacen = new AlmacenEnMemoria();
        var registro = new EquipmentAgreementLedger(almacen);
        var desenlace = new DesenlaceDelAlquiler(
            new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero), 120_000m, 280_000m, "admin-1", "sello-del-desenlace",
            "api:k-1", SelloPendiente: false);

        await registro.RecordAsync(new RentalAgreement(
            "pta-1", "u-1", "andamio", "Andamio", 2, new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 9),
            420_000m, 400_000m, "COP", new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero), string.Empty,
            CaminoDeCompra.Puerta, "api:k-1", SelloPendiente: true, RentalState.Returned, desenlace));

        var leido = await registro.GetAsync("pta-1");

        Assert.NotNull(leido);
        Assert.Equal(CaminoDeCompra.Puerta, leido.Camino);
        Assert.Equal("api:k-1", leido.SelladoPor);
        Assert.True(leido.SelloPendiente);
        Assert.Equal(RentalState.Returned, leido.Estado);
        Assert.Equal(desenlace, leido.Desenlace);
    }
}
