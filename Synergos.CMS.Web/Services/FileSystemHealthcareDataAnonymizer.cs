using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IHealthcareDataAnonymizer"/> (ADR 0098). De-identifica los
/// registros de paciente del miembro sobre <see cref="IPhiStore"/>: blanquea el
/// nombre, reduce la fecha de nacimiento a solo el año (HIPAA Safe-Harbor) y
/// desliga el <c>MemberKey</c>. Conserva <c>PatientKey</c> + contenido clínico
/// de-identificado (retención legal). NO borra el registro.
/// </summary>
public sealed class FileSystemHealthcareDataAnonymizer : IHealthcareDataAnonymizer
{
    private const string PatientsResource = "patients";
    private const string Redacted = "[de-identificado]";

    private readonly IPhiStore _store;

    public FileSystemHealthcareDataAnonymizer(IPhiStore store) => _store = store;

    public async Task<int> AnonymizeForMemberAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        if (memberKey == Guid.Empty) { return 0; }

        var raw = await _store.ListAsync(PatientsResource, cancellationToken).ConfigureAwait(false);
        var count = 0;
        foreach (var json in raw)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PatientRecord? record;
            try { record = JsonSerializer.Deserialize<PatientRecord>(json); }
            catch (JsonException) { continue; }
            if (record is null || record.MemberKey != memberKey) { continue; }

            var deidentified = record with
            {
                DisplayName = Redacted,
                MemberKey = Guid.Empty,                                                  // desliga del Member
                DateOfBirth = new DateTime(record.DateOfBirth.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc), // Safe-Harbor: solo año
            };
            await _store.WriteAsync(PatientsResource, record.PatientKey, JsonSerializer.Serialize(deidentified), cancellationToken)
                .ConfigureAwait(false);
            count++;
        }
        return count;
    }
}
