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
/// <para><b>Un sitio cambia claves sueltas, no la sección.</b> Cada entrada de <see cref="Sitios"/>
/// se nombra por la <c>Key</c> del siteRoot —la que viaja en uSync y no cambia al renombrar el
/// sitio— y lleva sólo lo que ese sitio cambia; lo demás lo hereda de los valores base. Nunca se
/// reemplaza la sección entera por sitio.</para>
///
/// <para><b>Una clave mal escrita falla al arrancar</b> (<c>ValidadorDeNegocioDeEventos</c>): el
/// binder de .NET descarta en silencio lo que no mapea, y sin el validador esta sección movería el
/// fallo silencioso del JSON del editor a un <c>appsettings</c>.</para>
/// </remarks>
public sealed class EventosFeatureSettings
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

    /// <summary>Lo que cambia cada sitio, por la <c>Key</c> de su siteRoot.</summary>
    public Dictionary<string, EventosFeatureSitio> Sitios { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Los valores que rigen en <paramref name="sitio"/>: los suyos encima de los base, clave por
    /// clave. Sin sitio, o con uno que no cambia nada, rigen los base.
    /// </summary>
    public NegocioDeEventos Para(Guid? sitio)
    {
        var propio = sitio is { } key ? DelSitio(key) : null;

        return new NegocioDeEventos(
            ApiBase: propio?.ApiBase ?? ApiBase,
            FeePercent: propio?.FeePercent ?? FeePercent,
            PlatformFeePercent: propio?.PlatformFeePercent ?? PlatformFeePercent);
    }

    /// <summary>
    /// Lo que cambia <paramref name="sitio"/>. La clave se compara como GUID y no como texto: con
    /// llaves o en mayúsculas sigue siendo la misma <c>Key</c>.
    /// </summary>
    private EventosFeatureSitio? DelSitio(Guid sitio)
    {
        foreach (var (clave, valores) in Sitios)
        {
            if (Guid.TryParse(clave, out var key) && key == sitio)
            {
                return valores;
            }
        }

        return null;
    }
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
