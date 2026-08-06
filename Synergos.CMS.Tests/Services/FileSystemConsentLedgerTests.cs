using Microsoft.Extensions.Logging.Abstractions;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemConsentLedger"/> (ADR 0098). Usa un
/// <see cref="IPhiStore"/> en memoria. Cubre grant→activo, revoke→inactivo,
/// expiración, doctor distinto e historial.
/// </summary>
public sealed class FileSystemConsentLedgerTests
{
    private readonly InMemoryPhiStore _store = new();
    private FileSystemConsentLedger BuildSut() => new(_store, NullLogger<FileSystemConsentLedger>.Instance);

    private static ConsentGrant Grant(Guid patient, Guid doctor, DateTime from, DateTime to) =>
        new(patient, doctor, from, to, "consultation");

    [Fact]
    public async Task Grant_Then_HasActiveConsent_True()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        var doctor = Guid.NewGuid();
        await sut.GrantAsync(Grant(patient, doctor, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)), CancellationToken.None);

        Assert.True(await sut.HasActiveConsentAsync(patient, doctor, CancellationToken.None));
    }

    [Fact]
    public async Task Revoke_Then_HasActiveConsent_False()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        var doctor = Guid.NewGuid();
        var id = await sut.GrantAsync(Grant(patient, doctor, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)), CancellationToken.None);

        await sut.RevokeAsync(id, CancellationToken.None);

        Assert.False(await sut.HasActiveConsentAsync(patient, doctor, CancellationToken.None));
    }

    [Fact]
    public async Task ExpiredConsent_NotActive()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        var doctor = Guid.NewGuid();
        await sut.GrantAsync(Grant(patient, doctor, DateTime.UtcNow.AddHours(-2), DateTime.UtcNow.AddHours(-1)), CancellationToken.None);

        Assert.False(await sut.HasActiveConsentAsync(patient, doctor, CancellationToken.None));
    }

    [Fact]
    public async Task DifferentDoctor_NotActive()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        await sut.GrantAsync(Grant(patient, Guid.NewGuid(), DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)), CancellationToken.None);

        Assert.False(await sut.HasActiveConsentAsync(patient, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetConsentHistory_FiltersByPatient_OrdersDesc()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        var other = Guid.NewGuid();
        await sut.GrantAsync(Grant(patient, Guid.NewGuid(), new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddYears(1)), CancellationToken.None);
        await sut.GrantAsync(Grant(patient, Guid.NewGuid(), new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddYears(1)), CancellationToken.None);
        await sut.GrantAsync(Grant(other, Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddYears(1)), CancellationToken.None);

        var history = await sut.GetConsentHistoryAsync(patient, CancellationToken.None);

        Assert.Equal(2, history.Count);   // solo los del paciente
        Assert.True(history[0].GrantedAtUtc > history[1].GrantedAtUtc);   // más reciente primero
    }

    [Fact]
    public async Task Revoke_Twice_Idempotent()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        var doctor = Guid.NewGuid();
        var id = await sut.GrantAsync(Grant(patient, doctor, DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddHours(1)), CancellationToken.None);

        await sut.RevokeAsync(id, CancellationToken.None);
        await sut.RevokeAsync(id, CancellationToken.None);   // segunda vez: no-op, no lanza

        Assert.False(await sut.HasActiveConsentAsync(patient, doctor, CancellationToken.None));
    }

    // IPhiStore en memoria (sin FS ni cifrado) para aislar la lógica del ledger.
    private sealed class InMemoryPhiStore : IPhiStore
    {
        private readonly Dictionary<string, string> _data = new();
        private static string K(string rt, Guid key) => rt + "/" + key.ToString("N");

        public Task WriteAsync(string resourceType, Guid key, string json, CancellationToken ct)
        {
            _data[K(resourceType, key)] = json;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.TryGetValue(K(resourceType, key), out var v) ? v : null);

        public Task<IReadOnlyList<string>> ListAsync(string resourceType, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<string>)_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => kv.Value).ToList());

        public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.Remove(K(resourceType, key)));
    }
}
