using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de la funcionalidad Eventos (ADR 0137) — sección
/// <c>Synergos:Features:Eventos</c>.
/// </summary>
/// <remarks>
/// <para><b>Es del despliegue, no del editor.</b> Antes de existir, la comisión era
/// <c>DEFAULT_FEE_PERCENT = 12</c> compilada en el bundle y sólo la cambiaba un editor tecleando
/// <c>feePercent</c> en el JSON libre del bloque; ningún camino del servidor la cobraba (#194).
/// Ahora la escribe quien despliega, una vez, y la leen lo que se muestra y lo que se cobra.</para>
///
/// <para>La forma —valores base, <c>Sitios</c> por la <c>Key</c> del siteRoot, fusión clave por
/// clave y validación al arrancar— es la de toda sección de negocio
/// (<see cref="SeccionDeNegocio{TSitio, TNegocio}"/>).</para>
/// </remarks>
public sealed class EventosFeatureSettings : SeccionDeNegocio<EventosFeatureSitio, NegocioDeEventos>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Eventos";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/eventos";

    /// <summary>Comisión de servicio que paga quien compra, sobre el subtotal, de 0 a 100.</summary>
    /// <remarks>El 12 es el que el bundle traía compilado: con los valores base nada cambia.</remarks>
    public decimal FeePercent { get; init; } = 12m;

    /// <summary>
    /// Comisión de la plataforma que se descuenta de lo que se le liquida al organizador, de 0 a 100.
    /// </summary>
    /// <remarks>El 10 es el que el bundle traía compilado en la plantilla del payout.</remarks>
    public decimal PlatformFeePercent { get; init; } = 10m;

    /// <inheritdoc />
    protected override NegocioDeEventos Fusionar(EventosFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            FeePercent: propio?.FeePercent ?? FeePercent,
            PlatformFeePercent: propio?.PlatformFeePercent ?? PlatformFeePercent);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeEventos negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            ReglasDeNegocio.Porcentaje("FeePercent", negocio.FeePercent),
            ReglasDeNegocio.Porcentaje("PlatformFeePercent", negocio.PlatformFeePercent),
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="EventosFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class EventosFeatureSitio
{
    /// <inheritdoc cref="EventosFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="EventosFeatureSettings.FeePercent"/>
    public decimal? FeePercent { get; init; }

    /// <inheritdoc cref="EventosFeatureSettings.PlatformFeePercent"/>
    public decimal? PlatformFeePercent { get; init; }
}
