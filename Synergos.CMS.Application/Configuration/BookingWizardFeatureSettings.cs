using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de el asistente de reservas (ADR 0137) — sección <c>Synergos:Features:BookingWizard</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class BookingWizardFeatureSettings : SeccionDeNegocio<BookingWizardFeatureSitio, NegocioDeBookingWizard>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:BookingWizard";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/booking";

    /// <inheritdoc />
    protected override NegocioDeBookingWizard Fusionar(BookingWizardFeatureSitio? propio)
        => new(ApiBase: propio?.ApiBase ?? ApiBase);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeBookingWizard negocio)
        => new[] { ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase) }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="BookingWizardFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class BookingWizardFeatureSitio
{
    /// <inheritdoc cref="BookingWizardFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }
}
