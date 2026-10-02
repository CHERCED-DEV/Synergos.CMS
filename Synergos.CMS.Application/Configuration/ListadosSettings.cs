namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Cómo se escriben los listados que arma el servidor (#196, tanda D) — sección
/// <c>Synergos:Listados</c>.
/// </summary>
public sealed class ListadosSettings
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Listados";

    /// <summary>
    /// La zona horaria del sitio (IANA), en la que se dice el día de una fecha. Los catálogos
    /// guardan la hora en UTC; sin zona, un evento de la noche del 4 saldría el 5.
    /// </summary>
    public string ZonaHoraria { get; init; } = "America/Bogota";

    /// <summary>La zona, o <c>null</c> si el sistema no la conoce.</summary>
    public TimeZoneInfo? Zona()
        => TimeZoneInfo.TryFindSystemTimeZoneById(ZonaHoraria, out var zona) ? zona : null;
}
