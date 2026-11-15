namespace Synergos.CMS.Interfaces;

/// <summary>
/// La clave del miembro que tiene un correo, o <c>null</c> (003-salud-cita, I2 T056). Sólo la clave: quien la pide decide
/// qué hace con ella, y ningún dato de la cuenta sale de aquí.
/// </summary>
/// <remarks>
/// Y al revés, a dónde y a nombre de quién avisarle a un miembro por su clave (I8 P18): los avisos del consultorio al
/// paciente salen de su historia, que guarda la clave del miembro y no su correo.
/// </remarks>
public interface IBuscadorDeMiembros
{
    Task<Guid?> ClaveDelCorreoAsync(string correo, CancellationToken cancellationToken);

    /// <summary>El correo y el nombre del miembro de esa clave; <c>null</c> si no existe o no tiene correo.</summary>
    Task<ContactoDelMiembro?> ContactoAsync(Guid clave, CancellationToken cancellationToken);
}

/// <summary>A dónde y a nombre de quién se le avisa a un miembro.</summary>
public sealed record ContactoDelMiembro(string Correo, string Nombre);
