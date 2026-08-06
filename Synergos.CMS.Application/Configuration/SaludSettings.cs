namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Contra qué agenda la cita clínica (HU #25) — sección <c>Synergos:Salud</c>.
/// </summary>
/// <remarks>
/// <para><b>El default es el stub</b>, igual que en Tienda y por lo mismo: un clon limpio arranca
/// con el portal clínico funcionando sin levantar el orquestador ni sus capacidades.</para>
/// </remarks>
public sealed class SaludSettings
{
    /// <summary>
    /// <c>Stub</c> (default, el motor en proceso) o <c>Bff</c> (contra <c>Synergos.Bff.Salud</c>).
    /// </summary>
    public string Mode { get; init; } = "Stub";

    /// <summary>Dónde vive el orquestador.</summary>
    public string BaseUrl { get; init; } = "http://127.0.0.1:5301/";

    /// <summary>La llave compartida servicio↔servicio.</summary>
    public string ApiKey { get; init; } = string.Empty;

    // Acá vivía `ResourceIdPrefix`, una convención para adivinar el identificador del recurso
    // en Api.Booking. Se fue: ese identificador lo genera la capacidad, así que ninguna
    // convención podía acertarlo. El BFF lo resuelve desde el profesional (HU #25).

    /// <summary>Qué servicio se agenda. El EHR-lite tiene uno solo, así que sale de configuración.</summary>
    public string ServiceKind { get; init; } = "salud.servicio";

    /// <summary>El identificador de ese servicio.</summary>
    public string ServiceId { get; init; } = "consulta";

    /// <summary>El <c>Kind</c> con el que se nombra al paciente y al profesional.</summary>
    public string PatientKind { get; init; } = "salud.paciente";

    /// <inheritdoc cref="PatientKind" />
    public string ProfessionalKind { get; init; } = "salud.profesional";

    /// <summary>Segundos de espera. Agendar cruza varios servicios y NO es auxiliar.</summary>
    public int TimeoutSeconds { get; init; } = 30;
}
