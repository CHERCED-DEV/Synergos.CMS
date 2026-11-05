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
                Resumen = Desligar(record.Resumen, memberKey),
                PreRegistro = SinIdentificadores(record.PreRegistro),
            };
            await _store.WriteAsync(PatientsResource, record.PatientKey, JsonSerializer.Serialize(deidentified), cancellationToken)
                .ConfigureAwait(false);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Los datos del resumen que registró o validó el miembro quedan sin su clave (003-salud-cita, I2 T057): lo que el
    /// paciente declaró lleva su <c>MemberKey</c> como autor, y dejarlo lo volvería a ligar. Lo clínico se conserva.
    /// </summary>
    private static ResumenDeSalud? Desligar(ResumenDeSalud? resumen, Guid memberKey) =>
        resumen is null
            ? null
            : resumen with
            {
                Datos = resumen.Datos
                    .Select(d => d with
                    {
                        RegistradoPor = d.RegistradoPor == memberKey ? Guid.Empty : d.RegistradoPor,
                        ValidadoPor = d.ValidadoPor == memberKey ? Guid.Empty : d.ValidadoPor,
                    })
                    .ToList(),
            };

    /// <summary>
    /// Del pre-registro se van los que identifican: el documento, el teléfono, la dirección, el contacto de emergencia
    /// y el número de afiliación. La aseguradora, los medicamentos y el motivo son contenido de la atención y se
    /// conservan, como el resto de lo clínico.
    /// </summary>
    private static PreRegistro? SinIdentificadores(PreRegistro? pre) =>
        pre is null
            ? null
            : pre with
            {
                Documento = pre.Documento is null ? null : Redacted,
                Telefono = pre.Telefono is null ? null : Redacted,
                Direccion = pre.Direccion is null ? null : Redacted,
                ContactoDeEmergencia = pre.ContactoDeEmergencia is null ? null : Redacted,
                NumeroDeAfiliacion = pre.NumeroDeAfiliacion is null ? null : Redacted,
            };
}
