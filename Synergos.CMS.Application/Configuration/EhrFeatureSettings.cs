using Synergos.CMS.Interfaces;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La configuración de negocio de el portal clínico (ADR 0137) — sección <c>Synergos:Features:Ehr</c>.
/// </summary>
/// <remarks>
/// Antes, dónde vive la API estaba tres veces: campo del editor, valor por defecto en la vista
/// Razor y <c>DEFAULT_API_BASE</c> en el bundle. Medido el 2026-10-02 en la base: el editor nunca
/// lo cambió. Ahora es del despliegue, por sitio (#196).
/// </remarks>
public sealed class EhrFeatureSettings : SeccionDeNegocio<EhrFeatureSitio, NegocioDeEhr>
{
    /// <summary>La sección de configuración.</summary>
    public const string Seccion = "Synergos:Features:Ehr";

    /// <summary>Dónde vive la API de la funcionalidad: relativa al sitio o absoluta.</summary>
    public string ApiBase { get; init; } = "/api/ehr";

    /// <summary>
    /// El <c>Kind</c> del servicio que se agenda (003-salud-cita). Por defecto el de
    /// <see cref="SaludSettings.ServiceKind"/>, que es el que usa la ruta directa: los dos caminos cobran el
    /// mismo servicio salvo que un sitio declare otro.
    /// </summary>
    public string ServiceKind { get; init; } = new SaludSettings().ServiceKind;

    /// <summary>El identificador de ese servicio; por defecto el de <see cref="SaludSettings.ServiceId"/>.</summary>
    public string ServiceId { get; init; } = new SaludSettings().ServiceId;

    /// <inheritdoc />
    protected override NegocioDeEhr Fusionar(EhrFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            Servicio: new ServicioAgendable(propio?.ServiceKind ?? ServiceKind, propio?.ServiceId ?? ServiceId));

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeEhr negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            string.IsNullOrWhiteSpace(negocio.Servicio.Kind) || string.IsNullOrWhiteSpace(negocio.Servicio.Id)
                ? "ServiceKind y ServiceId: el servicio que se agenda no puede quedar vacío (sin él la cita no tiene copago que cotizar)."
                : null,
        }.OfType<string>();
}

/// <summary>
/// Lo que un sitio cambia de <see cref="EhrFeatureSettings"/>. Una clave sin valor la hereda.
/// </summary>
public sealed class EhrFeatureSitio
{
    /// <inheritdoc cref="EhrFeatureSettings.ApiBase"/>
    public string? ApiBase { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.ServiceKind"/>
    public string? ServiceKind { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.ServiceId"/>
    public string? ServiceId { get; init; }
}
