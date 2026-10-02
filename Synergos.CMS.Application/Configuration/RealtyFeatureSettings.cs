using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la funcionalidad Propiedades (ADR 0137) — sección
/// <c>Synergos:Features:Realty</c>.
/// </summary>
/// <remarks>
/// Antes la tasa del simulador era <c>DEFAULT_RATE = 12</c> compilada en el bundle, y sólo la
/// cambiaba un editor tecleando <c>defaultRate</c> en el JSON libre del bloque (medido el
/// 2026-10-02: ningún bloque lo hacía). Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class RealtyFeatureSettings : SeccionDeNegocio<RealtyFeatureSitio, NegocioDeRealty>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Realty";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/realty";

    /// <summary>La tasa E.A., en porcentaje, con la que arranca el simulador de hipoteca.</summary>
    /// <remarks>El 12 es el que el bundle traía compilado: con los valores base nada cambia.</remarks>
    public decimal DefaultRatePercent { get; init; } = 12m;

    /// <inheritdoc />
    protected override NegocioDeRealty Fusionar(RealtyFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            DefaultRatePercent: propio?.DefaultRatePercent ?? DefaultRatePercent);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeRealty negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            ReglasDeNegocio.Porcentaje("DefaultRatePercent", negocio.DefaultRatePercent),
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="RealtyFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class RealtyFeatureSitio
{
    /// <inheritdoc cref="RealtyFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="RealtyFeatureSettings.DefaultRatePercent"/>
    public decimal? DefaultRatePercent { get; init; }
}
