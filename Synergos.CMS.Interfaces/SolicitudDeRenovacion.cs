namespace Synergos.CMS.Interfaces;

/// <summary>
/// El paciente pide renovar una receta (003-salud-cita, I5 P14); el médico la aprueba —sale una receta nueva, firmada— o la
/// rechaza con su motivo. Es PHI (dice qué medicamento toma) y vive en el almacén cifrado de la historia.
/// </summary>
/// <param name="Estado"><c>pedida</c>, <c>aprobada</c> o <c>rechazada</c>.</param>
/// <param name="NuevaRecetaId">La receta que salió al aprobarla.</param>
public sealed record SolicitudDeRenovacion(
    Guid Id,
    Guid PatientKey,
    Guid RecetaId,
    string? Nota,
    string Estado,
    DateTime PedidaUtc,
    Guid? ResueltaPor = null,
    DateTime? ResueltaUtc = null,
    Guid? NuevaRecetaId = null,
    string? Motivo = null);

/// <summary>Las solicitudes de renovación, sobre el almacén PHI.</summary>
public interface IRenovaciones
{
    Task<SolicitudDeRenovacion?> GetAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<SolicitudDeRenovacion>> ListAsync(Guid patientKey, CancellationToken cancellationToken);

    Task GuardarAsync(SolicitudDeRenovacion solicitud, CancellationToken cancellationToken);
}
