namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// Typed POCO bound from <c>appsettings.*.json</c> section
/// <c>Synergos:Healthcare</c> (ADR 0098). Comportamiento del vertical clínico:
/// disclaimer obligatorio, zona horaria de la agenda, retención y 2FA de staff.
/// </summary>
/// <remarks>
/// El módulo es RECORD-KEEPER: registra historia/agenda/recetas, NO diagnostica
/// ni aconseja. El disclaimer se renderiza en cada página de Healthcare.
/// </remarks>
public sealed class HealthcareSettings
{
    /// <summary>
    /// Texto legal que se muestra en cada página clínica. Deja claro que el
    /// sistema registra, no aconseja — el profesional licenciado es responsable.
    /// </summary>
    public string ClinicalDisclaimerText { get; init; } =
        "Este sistema registra información médica pero NO brinda consejo clínico. " +
        "Un profesional de la salud licenciado es responsable de todo diagnóstico y decisión.";

    /// <summary>Zona horaria para mostrar horarios de agenda (IANA). Default America/Bogota.</summary>
    public string DisplayTimezone { get; init; } = "America/Bogota";

    /// <summary>Minutos de sobrecupo permitidos al agendar. Default 0 (sin overbooking).</summary>
    public int MaxOverbookingMinutes { get; init; } = 0;

    /// <summary>Vigencia por defecto de una receta, en días. Default 365.</summary>
    public int PrescriptionValidityDays { get; init; } = 365;

    /// <summary>Retención de registros clínicos en días. Default 2190 (6 años). 0 = nunca purgar.</summary>
    public int RecordRetentionDays { get; init; } = 2190;

    /// <summary>Si true, los roles de staff (doctor/nurse/reception) requieren 2FA. Default true.</summary>
    public bool RequireTwoFactorStaff { get; init; } = true;
}
