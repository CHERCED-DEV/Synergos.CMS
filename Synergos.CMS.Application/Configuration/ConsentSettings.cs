namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Sección <c>Synergos:Consent</c>: de dónde lee el CMS los permisos de <c>Api.Consent</c> (003-salud-cita, I2 T054).
/// </summary>
/// <remarks>
/// <para>Con <c>Mode = Api</c> la guardia clínica lee el permiso del paciente en la capacidad (Q4: manda
/// <c>Api.Consent</c>). Sin <c>BaseUrl</c> no se adivina un puerto: el lector niega todo, que es fallar cerrado.</para>
/// <para>Con <c>Local</c> (el valor por defecto) la guardia sigue con el libro local de antes, médico por médico: es la
/// vuelta atrás y el desarrollo sin la capacidad. Producción la enciende al desplegar (002).</para>
/// </remarks>
public sealed class ConsentSettings
{
    public string Mode { get; init; } = "Local";
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public int TimeoutSeconds { get; init; } = 5;
}
