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

    /// <summary>
    /// La ruta del enlace del aviso de una estadía reservada en el sitio (D-006-AVISO): la página del sitio que abre el
    /// viaje, con sus marcadores (<c>/hoteles/?viaje={id}</c>). Sin valor, rige la ruta del flujo en la puerta.
    /// </summary>
    public string? AvisoRuta { get; init; }

    /// <inheritdoc />
    protected override NegocioDeBookingWizard Fusionar(BookingWizardFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            BookingPath: CaminoDeCompra.Normalizar(propio?.BookingPath ?? BookingPath),
            AvisoRuta: string.IsNullOrWhiteSpace(propio?.AvisoRuta ?? AvisoRuta) ? null : (propio?.AvisoRuta ?? AvisoRuta)!.Trim());

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeBookingWizard negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            ReglasDeNegocio.Camino("BookingPath", negocio.BookingPath),
            RutaDelSitio("AvisoRuta", negocio.AvisoRuta),
        }.OfType<string>();

    /// <summary>Una ruta del sitio (<c>/…</c>), nunca de otro origen; vacía vale (rige la del flujo).</summary>
    private static string? RutaDelSitio(string clave, string? ruta)
        => ruta is null || (ruta.Length > 1 && ruta[0] == '/' && !ruta.StartsWith("//", StringComparison.Ordinal))
            ? null
            : $"{clave} «{ruta}» tiene que ser una ruta del sitio (/…).";
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

    /// <inheritdoc cref="BookingWizardFeatureSettings.AvisoRuta"/>
    public string? AvisoRuta { get; init; }
}
