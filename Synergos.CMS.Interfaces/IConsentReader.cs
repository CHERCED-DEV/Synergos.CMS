namespace Synergos.CMS.Interfaces;

/// <summary>
/// Lee en <c>Api.Consent</c> si un sujeto tiene un permiso vigente para un propósito (003-salud-cita, I2 T054;
/// D-003-I2-1). Genérico a propósito: no sabe de historias ni de pacientes, sólo de sujetos y propósitos.
/// </summary>
/// <remarks>
/// <b>Nunca lanza.</b> Lo que no se pudo saber —la capacidad caída, sin configurar, una respuesta ilegible— es
/// <see cref="EstadoDelPermiso.NoSePudoLeer"/>, y quien decide un acceso con él NIEGA: no se abre nada sin un permiso
/// comprobado.
/// </remarks>
public interface IConsentReader
{
    Task<PermisoLeido> LeerAsync(string sujetoKind, string sujetoId, string proposito, CancellationToken cancellationToken);
}

/// <summary>Lo que contestó <c>Api.Consent</c>: el estado y, si está vigente, el id del permiso.</summary>
public sealed record PermisoLeido(EstadoDelPermiso Estado, string? PermisoId = null)
{
    public static PermisoLeido NoSePudo { get; } = new(EstadoDelPermiso.NoSePudoLeer);
    public static PermisoLeido NoVigente { get; } = new(EstadoDelPermiso.NoVigente);
}

public enum EstadoDelPermiso
{
    /// <summary>Hay un permiso vigente para ese propósito.</summary>
    Vigente,

    /// <summary>No se dio, se retiró o venció.</summary>
    NoVigente,

    /// <summary>No se pudo saber. Quien decide un acceso con esto, niega.</summary>
    NoSePudoLeer,
}
