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

    /// <summary>
    /// Por dónde reserva el elemento: <c>puerta</c> o <c>directa</c> (<see cref="CaminoDeCompra"/>). Por defecto
    /// <c>directa</c> (línea 006, P5), como el portal de viajes: un sitio pasa a la puerta cuando lo declara.
    /// </summary>
    /// <remarks>Un sitio con el valor vacío vuelve a la regla (la puerta si abre <c>viajes.reserva</c>).</remarks>
    public string? BookingPath { get; init; } = CaminoDeCompra.Directa;

    /// <inheritdoc />
    protected override NegocioDeBookingWizard Fusionar(BookingWizardFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            BookingPath: CaminoDeCompra.Normalizar(propio?.BookingPath ?? BookingPath));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeBookingWizard negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            ReglasDeNegocio.Camino("BookingPath", negocio.BookingPath),
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="BookingWizardFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class BookingWizardFeatureSitio
{
    /// <inheritdoc cref="BookingWizardFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="BookingWizardFeatureSettings.BookingPath"/>
    public string? BookingPath { get; init; }
}
