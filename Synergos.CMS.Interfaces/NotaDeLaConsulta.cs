namespace Synergos.CMS.Interfaces;

/// <summary>
/// La nota de una consulta en formato SOAP (003-salud-cita, I3 P6): lo que cuenta el paciente, lo que se observa, el
/// análisis y el plan. Es PHI y vive en el almacén cifrado. Mientras no se firma, su autor la cambia; firmada, no se cambia
/// nunca: una corrección es otra nota.
/// </summary>
/// <param name="PatientKey">La historia a la que pertenece (no el miembro: la supresión desliga al miembro, ADR 0142).</param>
/// <param name="ReservaId">La cita de la que sale, si salió de una.</param>
/// <param name="AutorKey">El médico que la escribió.</param>
/// <param name="Firma"><c>null</c> mientras es borrador.</param>
public sealed record NotaDeLaConsulta(
    Guid Id,
    Guid PatientKey,
    string? ReservaId,
    string Subjetivo,
    string Objetivo,
    string Analisis,
    string Plan,
    Guid AutorKey,
    DateTime CreadaUtc,
    DateTime ActualizadaUtc,
    FirmaDeLaNota? Firma = null);

/// <summary>
/// La firma de una nota: el sello de su huella con la llave del consultorio (<c>Api.Signing /v1/seals</c>). El sello es
/// determinista y no vence, así que años después se comprueba con la misma huella.
/// </summary>
/// <param name="Huella">SHA-256 de la nota en su forma canónica, en hexadecimal.</param>
public sealed record FirmaDeLaNota(string Huella, string Sello, string LlaveId, string Llave, Guid FirmadaPor, DateTime FirmadaUtc);

/// <summary>Las notas de la consulta, sobre el almacén PHI.</summary>
public interface INotasDeLaConsulta
{
    Task<NotaDeLaConsulta?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<NotaDeLaConsulta>> ListAsync(Guid patientKey, CancellationToken cancellationToken);

    Task GuardarAsync(NotaDeLaConsulta nota, CancellationToken cancellationToken);
}
