using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de el portal de viajes (ADR 0137) — sección <c>Synergos:Features:TravelShell</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class TravelShellFeatureSettings : SeccionDeNegocio<TravelShellFeatureSitio, NegocioDeTravelShell>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:TravelShell";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/travel";

    /// <summary>
    /// Por dónde reserva el elemento: <c>puerta</c> o <c>directa</c> (<see cref="CaminoDeCompra"/>). Por defecto
    /// <c>directa</c> (línea 006, P5): integrar la puerta no cambia el camino de ningún sitio, aunque el despliegue le dé
    /// destino a <c>Bff.Viajes</c>; un sitio pasa a la puerta cuando lo declara, después de verificarla en vivo.
    /// </summary>
    /// <remarks>Un sitio con el valor vacío vuelve a la regla (la puerta si abre <c>viajes.reserva</c>).</remarks>
    public string? BookingPath { get; init; } = CaminoDeCompra.Directa;

    /// <inheritdoc />
    protected override NegocioDeTravelShell Fusionar(TravelShellFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            BookingPath: CaminoDeCompra.Normalizar(propio?.BookingPath ?? BookingPath));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeTravelShell negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            ReglasDeNegocio.Camino("BookingPath", negocio.BookingPath),
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="TravelShellFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class TravelShellFeatureSitio
{
    /// <inheritdoc cref="TravelShellFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="TravelShellFeatureSettings.BookingPath"/>
    public string? BookingPath { get; init; }
}
