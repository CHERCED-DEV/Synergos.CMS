using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IHealthcareDataAnonymizer"/> (ADR 0098 §5, enmendada por la ADR 0142). Lo llama la supresión
/// (<see cref="FileSystemGdprRtbfCoordinator"/>) antes de borrar la cuenta del miembro: DESLIGA al miembro de su
/// historia clínica, en la versión vigente y en todas las archivadas, y la historia queda completa y cifrada.
/// </summary>
/// <remarks>
/// <para><b>Ya no borra la identidad, a propósito.</b> La historia clínica se conserva por ley mientras dure su
/// retención (en Colombia, la Resolución 839 de 2017, art. 3), y la supresión no procede cuando hay un deber legal de
/// permanecer en la base de datos (Decreto 1377 de 2013, art. 9). De-identificarla la volvía inútil como historia del
/// paciente: no se le podía entregar ni a él ni a quien la pidiera con derecho. Ver ADR 0142.</para>
/// <para><b>Lo que hace:</b> quita la clave del miembro de cada versión (<c>MemberKey</c>) y de los datos del resumen
/// que registró o validó (sus claves de autor). Antes sólo tocaba la vigente, y las versiones archivadas
/// (<c>patient-history</c>) seguían ligadas a una cuenta que ya no existía.</para>
/// <para>El nombre de la interfaz se queda (superset): quien la llama no cambia.</para>
/// </remarks>
public sealed class FileSystemHealthcareDataAnonymizer : IHealthcareDataAnonymizer
{
    private const string PatientsResource = "patients";
    private const string HistoryResource = "patient-history";

    private readonly IPhiStore _store;

    public FileSystemHealthcareDataAnonymizer(IPhiStore store) => _store = store;

    /// <summary>Desliga al miembro de sus historias. Devuelve cuántos documentos (vigentes y archivados) cambió.</summary>
    public async Task<int> AnonymizeForMemberAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        if (memberKey == Guid.Empty) { return 0; }

        var count = 0;
        foreach (var resource in new[] { PatientsResource, HistoryResource })
        {
            var entries = await _store.ListEntriesAsync(resource, cancellationToken).ConfigureAwait(false);
            foreach (var (key, json) in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PatientRecord? record;
                try { record = JsonSerializer.Deserialize<PatientRecord>(json); }
                catch (JsonException) { continue; }
                if (record is null || !EstaLigada(record, memberKey)) { continue; }

                await _store.WriteAsync(resource, key, JsonSerializer.Serialize(Desligar(record, memberKey)), cancellationToken)
                    .ConfigureAwait(false);
                count++;
            }
        }
        return count;
    }

    /// <summary>Si la versión nombra al miembro: como dueño, o como autor o validador de un dato del resumen.</summary>
    private static bool EstaLigada(PatientRecord record, Guid memberKey) =>
        record.MemberKey == memberKey
        || (record.Resumen?.Datos.Any(d => d.RegistradoPor == memberKey || d.ValidadoPor == memberKey) ?? false);

    /// <summary>La misma versión sin la clave del miembro. Lo demás —nombre, nacimiento, notas, pre-registro— se queda.</summary>
    private static PatientRecord Desligar(PatientRecord record, Guid memberKey) => record with
    {
        MemberKey = record.MemberKey == memberKey ? Guid.Empty : record.MemberKey,
        Resumen = record.Resumen is null
            ? null
            : record.Resumen with
            {
                Datos = record.Resumen.Datos
                    .Select(d => d with
                    {
                        RegistradoPor = d.RegistradoPor == memberKey ? Guid.Empty : d.RegistradoPor,
                        ValidadoPor = d.ValidadoPor == memberKey ? Guid.Empty : d.ValidadoPor,
                    })
                    .ToList(),
            },
    };
}
