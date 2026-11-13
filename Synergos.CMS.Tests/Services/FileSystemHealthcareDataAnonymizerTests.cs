using System.Text.Json;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemHealthcareDataAnonymizer"/> (ADR 0098 §5, enmendada por la ADR 0142). Usa un
/// <see cref="IPhiStore"/> en memoria. La supresión DESLIGA al miembro de su historia —la vigente y todas las
/// archivadas— y la historia queda completa: se conserva por ley mientras dure su retención. Los de otros miembros
/// no se tocan.
/// </summary>
/// <remarks>
/// Las dos primeras pruebas decían lo contrario hasta la ADR 0142 (nombre «[de-identificado]», nacimiento sólo con el
/// año, pre-registro borrado): cambiaron con la decisión D-003-I2-5, no para que el código pasara.
/// </remarks>
public sealed class FileSystemHealthcareDataAnonymizerTests
{
    private readonly InMemoryPhiStore _store = new();
    private FileSystemHealthcareDataAnonymizer BuildSut() => new(_store);

    private async Task<PatientRecord> SeedPatient(Guid memberKey, string name)
    {
        var record = new PatientRecord(
            Guid.NewGuid(), memberKey, name, new DateTime(1985, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            "dolor de cabeza", "presión normal", "migraña", DateTime.UtcNow, Guid.NewGuid(), 1, false);
        await _store.WriteAsync("patients", record.PatientKey, JsonSerializer.Serialize(record), CancellationToken.None);
        return record;
    }

    [Fact] // ADR 0142: la historia se conserva completa; sólo deja de estar ligada a la cuenta que se borra.
    public async Task Anonymize_DesligaAlMiembro_YLaHistoriaQuedaCompleta()
    {
        var member = Guid.NewGuid();
        var seeded = await SeedPatient(member, "Juan Pérez");

        var count = await BuildSut().AnonymizeForMemberAsync(member, CancellationToken.None);

        Assert.Equal(1, count);
        var raw = await _store.ReadAsync("patients", seeded.PatientKey, CancellationToken.None);
        var after = JsonSerializer.Deserialize<PatientRecord>(raw!)!;
        Assert.Equal(Guid.Empty, after.MemberKey);                       // desligada de la cuenta
        Assert.Equal(seeded with { MemberKey = Guid.Empty }, after);     // y lo demás, tal cual
    }

    [Fact] // las versiones archivadas también: antes seguían ligadas a una cuenta que ya no existía.
    public async Task Anonymize_DesligaTambienLasVersionesArchivadas_YLasConservaCompletas()
    {
        var member = Guid.NewGuid();
        var repo = new FileSystemPatientRepository(_store);
        var key = await repo.UpsertAsync(new PatientRecord(
            Guid.Empty, member, "Juan Pérez", new DateTime(1985, 7, 15, 0, 0, 0, DateTimeKind.Utc),
            "dolor de cabeza", null, null, default, Guid.NewGuid(), 0, false), CancellationToken.None);
        var v1 = (await repo.GetAsync(key, CancellationToken.None))!;
        await repo.UpsertAsync(v1 with { FindingsNotes = "presión normal" }, CancellationToken.None);
        var v2 = (await repo.GetAsync(key, CancellationToken.None))!;
        await repo.UpsertAsync(v2 with { AssessmentNotes = "migraña" }, CancellationToken.None);

        var count = await BuildSut().AnonymizeForMemberAsync(member, CancellationToken.None);

        Assert.Equal(3, count); // la vigente y las dos archivadas
        var archivadas = await _store.ListAsync("patient-history", CancellationToken.None);
        Assert.Equal(2, archivadas.Count);
        Assert.All(archivadas.Select(j => JsonSerializer.Deserialize<PatientRecord>(j)!), r =>
        {
            Assert.Equal(Guid.Empty, r.MemberKey);
            Assert.Equal("Juan Pérez", r.DisplayName);
            Assert.Equal(new DateTime(1985, 7, 15, 0, 0, 0, DateTimeKind.Utc), r.DateOfBirth);
        });
        var todo = string.Concat(archivadas.Concat(await _store.ListAsync("patients", CancellationToken.None)));
        Assert.DoesNotContain(member.ToString(), todo, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] // 003-salud-cita, I2: el pre-registro es parte de la historia; lo que se va es la clave del miembro.
    public async Task Anonymize_ElPreRegistroYElResumenQuedanCompletos_SinLaClaveDelMiembro()
    {
        var member = Guid.NewGuid();
        var medico = Guid.NewGuid();
        var declarado = new DatoDeSalud("d-1", TiposDeDatoDeSalud.Alergia, "Penicilina", null, EstadosDelDato.Validado,
            "paciente", DateTime.UtcNow, member, DateTime.UtcNow, medico);
        var record = (await SeedPatient(member, "Juan Pérez")) with
        {
            Resumen = new ResumenDeSalud(new[] { declarado }),
            PreRegistro = new PreRegistro("CC 52.123.456", "300 123 4567", "Calle 1 # 2-3", "Ana, 300 765 4321", "EPS Sura",
                "AF-998", new[] { "Losartán 50 mg" }, "Control", DateTime.UtcNow),
        };
        await _store.WriteAsync("patients", record.PatientKey, JsonSerializer.Serialize(record), CancellationToken.None);

        await BuildSut().AnonymizeForMemberAsync(member, CancellationToken.None);

        var raw = await _store.ReadAsync("patients", record.PatientKey, CancellationToken.None);
        var after = JsonSerializer.Deserialize<PatientRecord>(raw!)!;
        var pre = after.PreRegistro!;
        Assert.Equal(
            ("CC 52.123.456", "300 123 4567", "Calle 1 # 2-3", "Ana, 300 765 4321", "EPS Sura", "AF-998", "Control"),
            (pre.Documento, pre.Telefono, pre.Direccion, pre.ContactoDeEmergencia, pre.Aseguradora, pre.NumeroDeAfiliacion,
                pre.MotivoDeLaVisita));
        Assert.Equal(new[] { "Losartán 50 mg" }, pre.Medicamentos);
        Assert.Equal("Juan Pérez", after.DisplayName);
        var dato = Assert.Single(after.Resumen!.Datos);
        Assert.Equal(("Penicilina", Guid.Empty, (Guid?)medico), (dato.Descripcion, dato.RegistradoPor, dato.ValidadoPor));
        Assert.DoesNotContain(member.ToString(), raw!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Anonymize_OtherMembers_Untouched()
    {
        var target = Guid.NewGuid();
        await SeedPatient(target, "Objetivo");
        var otherPatient = await SeedPatient(Guid.NewGuid(), "Otro");

        var count = await BuildSut().AnonymizeForMemberAsync(target, CancellationToken.None);

        Assert.Equal(1, count);
        var raw = await _store.ReadAsync("patients", otherPatient.PatientKey, CancellationToken.None);
        var after = JsonSerializer.Deserialize<PatientRecord>(raw!)!;
        Assert.Equal("Otro", after.DisplayName);   // intacto
    }

    [Fact]
    public async Task Anonymize_EmptyMember_NoOp() =>
        Assert.Equal(0, await BuildSut().AnonymizeForMemberAsync(Guid.Empty, CancellationToken.None));

    [Fact]
    public async Task Anonymize_NoMatch_ReturnsZero() =>
        Assert.Equal(0, await BuildSut().AnonymizeForMemberAsync(Guid.NewGuid(), CancellationToken.None));

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

        public Task<IReadOnlyList<KeyValuePair<Guid, string>>> ListEntriesAsync(string resourceType, CancellationToken ct) =>
            Task.FromResult((IReadOnlyList<KeyValuePair<Guid, string>>)_data
                .Where(kv => kv.Key.StartsWith(resourceType + "/", StringComparison.Ordinal))
                .Select(kv => new KeyValuePair<Guid, string>(Guid.Parse(kv.Key[(resourceType.Length + 1)..]), kv.Value))
                .ToList());

        public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.Remove(K(resourceType, key)));
    }
}
