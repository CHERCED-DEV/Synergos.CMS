namespace Synergos.CMS.Interfaces;

/// <summary>
/// La orden médica (003-salud-cita, I4 P11): el médico ordena exámenes de laboratorio, imágenes o una remisión. Es PHI —el
/// nombre de un examen delata un diagnóstico— y vive en el almacén cifrado de la historia (Q10, ADR 0098).
/// </summary>
/// <param name="Tipo"><c>laboratorio</c>, <c>imagenes</c> o <c>remision</c>.</param>
/// <param name="Estado"><c>abierta</c> hasta que cada examen tiene su resultado liberado; entonces <c>cumplida</c>.</param>
public sealed record OrdenMedica(
    Guid Id,
    Guid PatientKey,
    string Tipo,
    IReadOnlyList<ExamenOrdenado> Examenes,
    string? Indicacion,
    Guid MedicoKey,
    DateTime CreadaUtc,
    string Estado);

/// <summary>Un examen de la orden.</summary>
public sealed record ExamenOrdenado(string Id, string Nombre);

/// <summary>
/// El resultado de un examen ordenado (I4 P12): lo carga el consultorio, con el valor, la unidad, el rango y la marca del
/// informe del laboratorio, y el PDF si lo hay. El paciente lo ve sólo cuando el médico lo libera tras revisarlo.
/// </summary>
/// <param name="Marca"><c>normal</c>, <c>alto</c>, <c>bajo</c> o <c>critico</c>, como la trae el informe.</param>
/// <param name="ArchivoId">El PDF del informe en el almacén privado cifrado (<see cref="IPrivateFileStore"/>).</param>
public sealed record ResultadoDeExamen(
    Guid Id,
    Guid OrdenId,
    Guid PatientKey,
    string ExamenId,
    string Valor,
    string? Unidad,
    string? Rango,
    string Marca,
    string? Nota,
    string? ArchivoId,
    string? ArchivoNombre,
    Guid CargadoPor,
    DateTime CargadoUtc,
    Guid? LiberadoPor = null,
    DateTime? LiberadoUtc = null)
{
    public bool Liberado => LiberadoUtc is not null;
}

/// <summary>Las órdenes y sus resultados, sobre el almacén PHI.</summary>
public interface IOrdenesMedicas
{
    Task<OrdenMedica?> GetOrdenAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<OrdenMedica>> ListOrdenesAsync(Guid patientKey, CancellationToken cancellationToken);

    Task GuardarOrdenAsync(OrdenMedica orden, CancellationToken cancellationToken);

    Task<ResultadoDeExamen?> GetResultadoAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ResultadoDeExamen>> ListResultadosAsync(Guid patientKey, CancellationToken cancellationToken);

    Task GuardarResultadoAsync(ResultadoDeExamen resultado, CancellationToken cancellationToken);
}
