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

    private static PatientRecord PatientOf(Guid memberKey, string name, bool deleted = false, DateTime created = default) =>
        new(Guid.Empty, memberKey, name, new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            "motivo", "hallazgos", "valoración", created, Guid.NewGuid(), 0, deleted);

    // ── Resolución miembro → clave clínica (portal del paciente, ADR 0120) ──
    //
    // Es lo que permite que un paciente llegue a su propio expediente: el permiso ya existía
    // en el guard, pero los endpoints se direccionan por PatientKey, que es DISTINTA del
    // MemberKey a propósito y que nadie le dice al paciente.

    [Fact]
    public async Task FindKeyByMember_DevuelveLaClaveDelExpedienteVinculado()
    {
        var sut = BuildSut();
        var memberKey = Guid.NewGuid();
        var key = await sut.UpsertAsync(PatientOf(memberKey, "Camila"), CancellationToken.None);
        await sut.UpsertAsync(PatientOf(Guid.NewGuid(), "Otro paciente"), CancellationToken.None);

        Assert.Equal(key, await sut.FindKeyByMemberAsync(memberKey, CancellationToken.None));
    }

    [Fact]
    public async Task FindKeyByMember_SinExpediente_DevuelveNull()
    {
        var sut = BuildSut();
        await sut.UpsertAsync(PatientOf(Guid.NewGuid(), "Ajeno"), CancellationToken.None);

        Assert.Null(await sut.FindKeyByMemberAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task FindKeyByMember_GuidEmpty_NuncaCasaConNada()
    {
        // Guid.Empty es "sin miembro", no un miembro. Sin el corte, cualquier expediente
        // creado sin vincular casaría con él y una sesión mal manejada se llevaría una
        // historia clínica ajena.
        var sut = BuildSut();
        await sut.UpsertAsync(PatientOf(Guid.Empty, "Sin vincular"), CancellationToken.None);

        Assert.Null(await sut.FindKeyByMemberAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task FindKeyByMember_IgnoraLosBorrados()
    {
        // Un expediente archivado no le devuelve el portal a nadie.
        var sut = BuildSut();
        var memberKey = Guid.NewGuid();
        await sut.UpsertAsync(PatientOf(memberKey, "Archivado", deleted: true), CancellationToken.None);

        Assert.Null(await sut.FindKeyByMemberAsync(memberKey, CancellationToken.None));
    }

    [Fact]
    public async Task FindKeyByMember_ConDosVigentes_DevuelveElMasReciente()
    {
        // Dos expedientes vigentes por miembro es dato corrupto, no un caso de negocio. Se
        // sirve el más nuevo en vez de fallar: negarle el portal a un paciente por una
        // inconsistencia del almacén es peor.
        var sut = BuildSut();
        var memberKey = Guid.NewGuid();
        await sut.UpsertAsync(
            PatientOf(memberKey, "Viejo", created: new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);
        var nuevo = await sut.UpsertAsync(
            PatientOf(memberKey, "Nuevo", created: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
            CancellationToken.None);

        Assert.Equal(nuevo, await sut.FindKeyByMemberAsync(memberKey, CancellationToken.None));
    }

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
