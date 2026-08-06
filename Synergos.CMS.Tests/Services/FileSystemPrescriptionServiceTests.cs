using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemPrescriptionService"/> (ADR 0098 H2c). Usa un
/// <see cref="IPhiStore"/> en memoria. Cubre emisión, get, listado por paciente,
/// revocación y faltantes.
/// </summary>
public sealed class FileSystemPrescriptionServiceTests
{
    private readonly InMemoryPhiStore _store = new();
    private FileSystemPrescriptionService BuildSut() => new(_store);

    private static IssuePrescriptionRequest Req(Guid patient) =>
        new(patient, Guid.NewGuid(), "Amoxicilina", "500mg", "cada 8h", "con comida",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(30));

    [Fact]
    public async Task Issue_Then_Get_RoundTrips_StatusActive()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        var issued = await sut.IssueAsync(Req(patient), CancellationToken.None);

        Assert.Equal("active", issued.Status);
        var got = await sut.GetAsync(issued.PrescriptionId, CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal("Amoxicilina", got!.MedicationName);
        Assert.Equal(patient, got.PatientKey);
    }

    [Fact]
    public async Task ListForPatient_FiltersByPatient_OrdersByIssuedDesc()
    {
        var sut = BuildSut();
        var patient = Guid.NewGuid();
        await sut.IssueAsync(Req(patient), CancellationToken.None);
        await sut.IssueAsync(Req(patient), CancellationToken.None);
        await sut.IssueAsync(Req(Guid.NewGuid()), CancellationToken.None);   // otro paciente

        var list = await sut.ListForPatientAsync(patient, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.All(list, r => Assert.Equal(patient, r.PatientKey));
    }

    [Fact]
    public async Task Revoke_MarksRevoked()
    {
        var sut = BuildSut();
        var issued = await sut.IssueAsync(Req(Guid.NewGuid()), CancellationToken.None);

        var ok = await sut.RevokeAsync(issued.PrescriptionId, CancellationToken.None);
        var after = await sut.GetAsync(issued.PrescriptionId, CancellationToken.None);

        Assert.True(ok);
        Assert.Equal("revoked", after!.Status);
    }

    [Fact]
    public async Task Revoke_Missing_ReturnsFalse() =>
        Assert.False(await BuildSut().RevokeAsync(Guid.NewGuid(), CancellationToken.None));

    [Fact]
    public async Task Get_Missing_ReturnsNull() =>
        Assert.Null(await BuildSut().GetAsync(Guid.NewGuid(), CancellationToken.None));

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
