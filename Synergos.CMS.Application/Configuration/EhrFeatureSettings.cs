using Synergos.CMS.Application.Services.Impl;
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

    /// <summary>
    /// El override de negocio del camino de la cita (003-salud-cita, P5): <c>puerta</c> o <c>directa</c>. Vacío,
    /// decide la regla de <see cref="CaminoDeCompra"/> —la puerta si abre <c>salud.cita</c>, si no la directa—,
    /// la misma que Eventos. Un valor mal escrito no deja arrancar.
    /// </summary>
    public string? AppointmentPath { get; init; }

    /// <summary>
    /// La base de las lecturas genéricas de Salud (003-salud-cita, I1 de producción): el directorio de profesionales con
    /// forma neutra de vertical, como la tienda (014-despiece-tienda, I4).
    /// </summary>
    /// <remarks>
    /// Vacía, el portal vuelve a leer las rutas de <see cref="ApiBase"/> (<c>api/ehr</c>, de desarrollo): es la vuelta
    /// atrás sin desplegar.
    /// </remarks>
    public string LecturasBase { get; init; } = "/api/lecturas/" + CatalogoDeSalud.Vertical;

    /// <summary>
    /// La versión del texto del permiso de datos de salud (003-salud-cita, T048-T049): la del texto de la sección
    /// <c>Health.Consent</c> del diccionario que ve quien lo da. Llega a <c>Api.Consent</c> con el permiso, como prueba de
    /// qué se aceptó (Ley 1581, art. 9).
    /// </summary>
    /// <remarks>
    /// <b>Cambiar el texto obliga a subir la versión</b>: un permiso dado con otro texto se reconoce por la suya. Vacía no
    /// deja arrancar. La de hoy es la del borrador: el texto lo aprueba el arquitecto antes de encender Salud (S-4).
    /// </remarks>
    public string VersionDelPermiso { get; init; } = "2026-11-borrador";

    /// <inheritdoc />
    protected override NegocioDeEhr Fusionar(EhrFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            Servicio: new ServicioAgendable(propio?.ServiceKind ?? ServiceKind, propio?.ServiceId ?? ServiceId),
            AppointmentPath: CaminoDeCompra.Normalizar(propio?.AppointmentPath ?? AppointmentPath),
            LecturasBase: (propio?.LecturasBase ?? LecturasBase).Trim(),
            VersionDelPermiso: (propio?.VersionDelPermiso ?? VersionDelPermiso).Trim());

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeEhr negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            // Vacía vale: es la vuelta atrás.
            string.IsNullOrWhiteSpace(negocio.LecturasBase) ? null : ReglasDeNegocio.ApiBase("LecturasBase", negocio.LecturasBase),
            ReglasDeNegocio.Camino("AppointmentPath", negocio.AppointmentPath),
            string.IsNullOrWhiteSpace(negocio.VersionDelPermiso)
                ? "VersionDelPermiso: no puede quedar vacía (sin ella, un permiso no dice qué texto se aceptó)."
                : null,
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

    /// <inheritdoc cref="EhrFeatureSettings.AppointmentPath"/>
    public string? AppointmentPath { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.LecturasBase"/>
    public string? LecturasBase { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.VersionDelPermiso"/>
    public string? VersionDelPermiso { get; init; }
}
