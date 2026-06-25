using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemPatientRepository"/> (ADR 0098 H2). Usa un
/// <see cref="IPhiStore"/> en memoria. Cubre alta+get, versionado append-only,
/// listado (excluye borrados, filtra por doctor) y get inexistente.
/// </summary>
public sealed class FileSystemPatientRepositoryTests
{
    private readonly InMemoryPhiStore _store = new();
    private FileSystemPatientRepository BuildSut() => new(_store);

    private static PatientRecord Patient(Guid key, string name, Guid doctor, bool deleted = false) =>
        new(key, Guid.NewGuid(), name, new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "motivo", "hallazgos", "valoración", default, doctor, 0, deleted);

    [Fact]
    public async Task Upsert_New_AssignsKey_And_Get_RoundTrips()
    {
        var sut = BuildSut();
        var key = await sut.UpsertAsync(Patient(Guid.Empty, "Ana", Guid.NewGuid()), CancellationToken.None);

        Assert.NotEqual(Guid.Empty, key);
        var got = await sut.GetAsync(key, CancellationToken.None);
        Assert.NotNull(got);
        Assert.Equal("Ana", got!.DisplayName);
        Assert.Equal(1, got.VersionNumber);
        Assert.NotEqual(default, got.CreatedAtUtc);
    }

    [Fact]
    public async Task Upsert_Existing_IncrementsVersion_PreservesCreatedAt()
    {
        var sut = BuildSut();
        var key = await sut.UpsertAsync(Patient(Guid.Empty, "Beto", Guid.NewGuid()), CancellationToken.None);
        var v1 = await sut.GetAsync(key, CancellationToken.None);

        await sut.UpsertAsync(v1! with { FindingsNotes = "actualizado" }, CancellationToken.None);
        var v2 = await sut.GetAsync(key, CancellationToken.None);

        Assert.Equal(2, v2!.VersionNumber);
        Assert.Equal(v1!.CreatedAtUtc, v2.CreatedAtUtc);     // creación original preservada
        Assert.Equal("actualizado", v2.FindingsNotes);
    }

    [Fact]
    public async Task List_ExcludesDeleted_AndMapsSummary()
    {
        var sut = BuildSut();
        await sut.UpsertAsync(Patient(Guid.Empty, "Vigente", Guid.NewGuid()), CancellationToken.None);
        await sut.UpsertAsync(Patient(Guid.Empty, "Borrado", Guid.NewGuid(), deleted: true), CancellationToken.None);

        var list = await sut.ListAsync(new PatientQuery(), CancellationToken.None);

        var only = Assert.Single(list);
        Assert.Equal("Vigente", only.DisplayName);
        Assert.True(only.AgeYears > 0);
    }

    [Fact]
    public async Task List_FilterByDoctor()
    {
        var sut = BuildSut();
        var doctorA = Guid.NewGuid();
        await sut.UpsertAsync(Patient(Guid.Empty, "Del A", doctorA), CancellationToken.None);
        await sut.UpsertAsync(Patient(Guid.Empty, "Del B", Guid.NewGuid()), CancellationToken.None);

        var list = await sut.ListAsync(new PatientQuery(DoctorKey: doctorA), CancellationToken.None);

        var only = Assert.Single(list);
        Assert.Equal("Del A", only.DisplayName);
    }

    [Fact]
    public async Task Get_Missing_ReturnsNull()
    {
        Assert.Null(await BuildSut().GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

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
