using System.Text.Json;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Default <see cref="IPatientRepository"/> (ADR 0098). Persiste sobre
/// <see cref="IPhiStore"/> (cifrado, atómico): la versión vigente en
/// <c>patients</c> y cada versión previa archivada en <c>patient-history</c>
/// (rastro inmutable). No valida acceso — eso es del <see cref="IPhiAccessGuard"/>.
/// </summary>
public sealed class FileSystemPatientRepository : IPatientRepository
{
    private const string Current = "patients";
    private const string History = "patient-history";

    private readonly IPhiStore _store;

    public FileSystemPatientRepository(IPhiStore store) => _store = store;

    public async Task<PatientRecord?> GetAsync(Guid patientKey, CancellationToken cancellationToken)
    {
        var json = await _store.ReadAsync(Current, patientKey, cancellationToken).ConfigureAwait(false);
        return json is null ? null : Deserialize(json);
    }

    public async Task<Guid> UpsertAsync(PatientRecord record, CancellationToken cancellationToken)
    {
        var patientKey = record.PatientKey == Guid.Empty ? Guid.NewGuid() : record.PatientKey;
        var current = await GetAsync(patientKey, cancellationToken).ConfigureAwait(false);

        int version;
        DateTime createdAt;
        if (current is not null)
        {
            // Archiva la versión previa (inmutable) antes de sobreescribir la vigente.
            await _store.WriteAsync(History, Guid.NewGuid(), JsonSerializer.Serialize(current), cancellationToken)
                .ConfigureAwait(false);
            version = current.VersionNumber + 1;
            createdAt = current.CreatedAtUtc;   // se preserva la creación original
        }
        else
        {
            version = 1;
            createdAt = record.CreatedAtUtc == default ? DateTime.UtcNow : record.CreatedAtUtc;
        }

        var toStore = record with { PatientKey = patientKey, VersionNumber = version, CreatedAtUtc = createdAt };
        await _store.WriteAsync(Current, patientKey, JsonSerializer.Serialize(toStore), cancellationToken)
            .ConfigureAwait(false);
        return patientKey;
    }

    public async Task<IReadOnlyList<PatientSummary>> ListAsync(PatientQuery query, CancellationToken cancellationToken)
    {
        var raw = await _store.ListAsync(Current, cancellationToken).ConfigureAwait(false);
        var result = new List<PatientSummary>();
        foreach (var json in raw)
        {
            var r = Deserialize(json);
            if (r is null || r.IsDeleted) continue;
            if (query.DoctorKey is Guid dk && r.CreatedByDoctorKey != dk) continue;
            result.Add(new PatientSummary(r.PatientKey, r.DisplayName, AgeYears(r.DateOfBirth), r.CreatedAtUtc, r.VersionNumber));
        }
        var limit = query.Limit <= 0 ? 200 : query.Limit;
        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).Take(limit).ToList();
    }

    /// <summary>
    /// La clave clínica vinculada a un miembro, o null. Barre el espacio vigente.
    /// </summary>
    /// <remarks>
    /// <b>Un barrido y no un índice</b>, a propósito. Un índice <c>memberKey → patientKey</c>
    /// sería un segundo lugar donde vive el vínculo, y de los dos el archivo del paciente es
    /// el que manda: un índice desincronizado le mostraría a alguien el expediente de otro, y
    /// esa es la peor falla posible de este vertical. El barrido lee de la única fuente y no
    /// puede mentir. Si algún día el volumen lo exige, el índice se añade JUNTO con su
    /// invalidador — la misma regla que el resto del repo aplica a las cachés.
    ///
    /// <para>Los borrados lógicos se saltan: un expediente archivado no le devuelve el portal
    /// a nadie.</para>
    /// </remarks>
    public async Task<Guid?> FindKeyByMemberAsync(Guid memberKey, CancellationToken cancellationToken)
    {
        if (memberKey == Guid.Empty)
        {
            // Guid.Empty es "sin miembro", no un miembro. Sin este corte, cualquier expediente
            // creado sin vincular casaría con él y el primer anónimo mal manejado se llevaría
            // una historia clínica ajena.
            return null;
        }

        var raw = await _store.ListAsync(Current, cancellationToken).ConfigureAwait(false);

        PatientRecord? best = null;
        foreach (var json in raw)
        {
            var record = Deserialize(json);
            if (record is null || record.IsDeleted || record.MemberKey != memberKey)
            {
                continue;
            }

            // Más de un expediente vigente por miembro es dato corrupto, no un caso de
            // negocio. Se sirve el más reciente en vez de fallar: negarle el portal a un
            // paciente por una inconsistencia del almacén es peor que darle el más nuevo.
            if (best is null || record.CreatedAtUtc > best.CreatedAtUtc)
            {
                best = record;
            }
        }

        return best?.PatientKey;
    }

    private static int AgeYears(DateTime dateOfBirth)
    {
        var today = DateTime.UtcNow.Date;
        var age = today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > today.AddYears(-age)) age--;
        return age < 0 ? 0 : age;
    }

    private static PatientRecord? Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<PatientRecord>(json); }
        catch (JsonException) { return null; }
    }
}
