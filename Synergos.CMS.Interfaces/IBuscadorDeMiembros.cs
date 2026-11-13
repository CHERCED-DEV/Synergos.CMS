namespace Synergos.CMS.Interfaces;

/// <summary>
/// La clave del miembro que tiene un correo, o <c>null</c> (003-salud-cita, I2 T056). Sólo la clave: quien la pide decide
/// qué hace con ella, y ningún dato de la cuenta sale de aquí.
/// </summary>
public interface IBuscadorDeMiembros
{
    Task<Guid?> ClaveDelCorreoAsync(string correo, CancellationToken cancellationToken);
}
