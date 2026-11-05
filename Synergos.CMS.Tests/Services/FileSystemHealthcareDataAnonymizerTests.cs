using System.Text.Json;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services;

namespace Synergos.CMS.Tests.Services;

/// <summary>
/// Tests para <see cref="FileSystemHealthcareDataAnonymizer"/> (ADR 0098 H3).
/// Usa un <see cref="IPhiStore"/> en memoria. Verifica que de-identifica los
/// registros del miembro (nombre, fecha → año, desliga MemberKey) conservando el
/// contenido clínico, y deja intactos los de otros miembros.
/// </summary>
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

    [Fact]
    public async Task Anonymize_DeIdentifiesMembersPatient_KeepsClinicalContent()
    {
        var member = Guid.NewGuid();
        var seeded = await SeedPatient(member, "Juan Pérez");

        var count = await BuildSut().AnonymizeForMemberAsync(member, CancellationToken.None);

        Assert.Equal(1, count);
        var raw = await _store.ReadAsync("patients", seeded.PatientKey, CancellationToken.None);
        var after = JsonSerializer.Deserialize<PatientRecord>(raw!)!;
        Assert.Equal("[de-identificado]", after.DisplayName);            // nombre blanqueado
        Assert.Equal(Guid.Empty, after.MemberKey);                       // desligado del Member
        Assert.Equal(new DateTime(1985, 1, 1, 0, 0, 0, DateTimeKind.Utc), after.DateOfBirth);  // solo año
        Assert.Equal("migraña", after.AssessmentNotes);                  // contenido clínico conservado
        Assert.Equal(seeded.PatientKey, after.PatientKey);               // PatientKey intacto
    }

    [Fact] // 003-salud-cita, I2 T057: lo que el paciente declaró lleva su clave y lo que lo identifica.
    public async Task Anonymize_ElPreRegistroYLoDeclarado_PierdenLoQueIdentifica_YConservanLoClinico()
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
        Assert.All(new[] { pre.Documento, pre.Telefono, pre.Direccion, pre.ContactoDeEmergencia, pre.NumeroDeAfiliacion },
            campo => Assert.Equal("[de-identificado]", campo));
        Assert.Equal(("EPS Sura", "Control"), (pre.Aseguradora, pre.MotivoDeLaVisita));
        Assert.Equal(new[] { "Losartán 50 mg" }, pre.Medicamentos);
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

        public Task<bool> DeleteAsync(string resourceType, Guid key, CancellationToken ct) =>
            Task.FromResult(_data.Remove(K(resourceType, key)));
    }
}
