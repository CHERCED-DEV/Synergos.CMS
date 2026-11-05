using System.Collections.Concurrent;
using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Lee, cambia y guarda una historia clínica bajo un candado por paciente (003-salud-cita, I2 T057), como
/// <see cref="LockingAppointmentScheduler"/> hace con la agenda de cada médico.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> <see cref="IPatientRepository.UpsertAsync"/> guarda la historia ENTERA. Con el
/// resumen dentro, el pre-registro del paciente y la validación del médico son dos lecturas-y-escrituras sobre el
/// mismo registro: sin candado, la que llega segunda pisa a la primera, y una validación o una alergia declarada se
/// pierde de la versión vigente sin que nadie lo note.</para>
/// <para>No decide acceso: eso es de <see cref="IPhiAccessGuard"/>, antes de llamar aquí. Un proceso, un candado: el
/// CMS corre en un solo origen (como la agenda).</para>
/// </remarks>
public sealed class EditorDeLaHistoriaClinica
{
    private readonly IPatientRepository _patients;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _candados = new();

    public EditorDeLaHistoriaClinica(IPatientRepository patients) => _patients = patients;

    /// <summary>
    /// Aplica <paramref name="cambio"/> a la versión vigente y la guarda como una versión nueva. Si el cambio devuelve
    /// la MISMA instancia, no hay nada que guardar y no se crea versión. <c>null</c> si la historia no existe o está
    /// borrada.
    /// </summary>
    public async Task<PatientRecord?> ModificarAsync(
        Guid patientKey,
        Func<PatientRecord, PatientRecord> cambio,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cambio);
        var candado = _candados.GetOrAdd(patientKey, static _ => new SemaphoreSlim(1, 1));
        await candado.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actual = await _patients.GetAsync(patientKey, cancellationToken).ConfigureAwait(false);
            if (actual is null || actual.IsDeleted)
            {
                return null;
            }

            var nueva = cambio(actual);
            if (ReferenceEquals(nueva, actual))
            {
                return actual;
            }

            // La clave es la de la historia que se leyó: un cambio no puede mudarla a otra.
            await _patients.UpsertAsync(nueva with { PatientKey = patientKey }, cancellationToken).ConfigureAwait(false);
            return await _patients.GetAsync(patientKey, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            candado.Release();
        }
    }
}
