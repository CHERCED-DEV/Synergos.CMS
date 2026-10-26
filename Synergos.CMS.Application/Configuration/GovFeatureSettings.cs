using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de el portal de trámites (ADR 0137) — sección <c>Synergos:Features:Gov</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class GovFeatureSettings : SeccionDeNegocio<GovFeatureSitio, NegocioDeGov>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Gov";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/gov";

    /// <summary>
    /// Por dónde se radica: <c>puerta</c> (el flujo <c>gobierno.radicacion</c>, con la tasa por el orquestador) o
    /// <c>directa</c> (la ruta del portal, como siempre). Sin valor no declara nada y decide la entrada de la puerta
    /// (<c>Synergos:Puerta:Flujos:gobierno.radicacion:Camino</c>, «directa» en el <c>appsettings.json</c> base): un sitio
    /// declara <c>puerta</c> después de verificarla en vivo (009-gobierno-tramite T033).
    /// </summary>
    public string? FilingPath { get; init; }

    /// <inheritdoc />
    protected override NegocioDeGov Fusionar(GovFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            FilingPath: CaminoDelFlujo.Normalizar(propio?.FilingPath ?? FilingPath));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeGov negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            CaminoDelFlujo.EsValido(negocio.FilingPath)
                ? null
                : $"FilingPath: «{negocio.FilingPath}» tiene que ser «{CaminoDelFlujo.Puerta}» o «{CaminoDelFlujo.Directa}», o no estar (y decide la entrada de la puerta).",
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="GovFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class GovFeatureSitio
{
    /// <inheritdoc cref="GovFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="GovFeatureSettings.FilingPath"/>
    public string? FilingPath { get; init; }
}
