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

    /// <summary>
    /// La base de la historia clínica de producción (003-salud-cita, I2 T062): «Mi salud», el check-in y las vistas del
    /// personal leen y escriben en <c>/api/healthcare</c>. Vacía, el portal vuelve a lo de <see cref="ApiBase"/> (la
    /// vuelta atrás sin desplegar).
    /// </summary>
    public string HistoriaBase { get; init; } = "/api/healthcare";

    /// <summary>
    /// Cancelar una cita confirmada (003-salud-cita, I6, la ficha F4): desde cuántas horas antes de su inicio se devuelve el
    /// copago entero. Lo de hoy es la propuesta de la línea (D-003-I6-1): 24 h, como la costumbre de los consultorios.
    /// </summary>
    /// <remarks>
    /// <b>Una sola regla</b>: la puerta manda los tres valores a <c>salud.cancelacion</c> como negocio, el orquestador calcula
    /// con ellos lo que devuelve, y el elemento los recibe para decir lo mismo antes de cancelar. El navegador no los elige.
    /// </remarks>
    public int DevolucionTotalHoras { get; init; } = 24;

    /// <summary>Desde cuántas horas antes se devuelve <see cref="DevolucionParcialPorcentaje"/>; con menos, nada.</summary>
    public int DevolucionParcialHoras { get; init; } = 2;

    /// <summary>El porcentaje del copago que se devuelve entre <see cref="DevolucionParcialHoras"/> y <see cref="DevolucionTotalHoras"/>.</summary>
    public int DevolucionParcialPorcentaje { get; init; } = 50;

    /// <inheritdoc />
    protected override NegocioDeEhr Fusionar(EhrFeatureSitio? propio)
        => new(
            ApiBase: propio?.ApiBase ?? ApiBase,
            Servicio: new ServicioAgendable(propio?.ServiceKind ?? ServiceKind, propio?.ServiceId ?? ServiceId),
            AppointmentPath: CaminoDeCompra.Normalizar(propio?.AppointmentPath ?? AppointmentPath),
            LecturasBase: (propio?.LecturasBase ?? LecturasBase).Trim(),
            VersionDelPermiso: (propio?.VersionDelPermiso ?? VersionDelPermiso).Trim(),
            HistoriaBase: (propio?.HistoriaBase ?? HistoriaBase).Trim(),
            DevolucionTotalHoras: propio?.DevolucionTotalHoras ?? DevolucionTotalHoras,
            DevolucionParcialHoras: propio?.DevolucionParcialHoras ?? DevolucionParcialHoras,
            DevolucionParcialPorcentaje: propio?.DevolucionParcialPorcentaje ?? DevolucionParcialPorcentaje);

    /// <inheritdoc />
    protected override IEnumerable<string> ProblemasDe(NegocioDeEhr negocio)
        => new[]
        {
            ReglasDeNegocio.ApiBase("ApiBase", negocio.ApiBase),
            // Vacía vale: es la vuelta atrás.
            string.IsNullOrWhiteSpace(negocio.LecturasBase) ? null : ReglasDeNegocio.ApiBase("LecturasBase", negocio.LecturasBase),
            string.IsNullOrWhiteSpace(negocio.HistoriaBase) ? null : ReglasDeNegocio.ApiBase("HistoriaBase", negocio.HistoriaBase),
            ReglasDeNegocio.Camino("AppointmentPath", negocio.AppointmentPath),
            string.IsNullOrWhiteSpace(negocio.VersionDelPermiso)
                ? "VersionDelPermiso: no puede quedar vacía (sin ella, un permiso no dice qué texto se aceptó)."
                : null,
            string.IsNullOrWhiteSpace(negocio.Servicio.Kind) || string.IsNullOrWhiteSpace(negocio.Servicio.Id)
                ? "ServiceKind y ServiceId: el servicio que se agenda no puede quedar vacío (sin él la cita no tiene copago que cotizar)."
                : null,
            negocio.DevolucionTotalHoras < 0 || negocio.DevolucionParcialHoras < 0
                ? "DevolucionTotalHoras y DevolucionParcialHoras: las horas no pueden ser negativas."
                : negocio.DevolucionParcialHoras > negocio.DevolucionTotalHoras
                    ? "DevolucionParcialHoras: la devolución parcial no puede empezar antes que la total."
                    : null,
            negocio.DevolucionParcialPorcentaje is < 0 or > 100
                ? "DevolucionParcialPorcentaje: va de 0 a 100."
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

    /// <inheritdoc cref="EhrFeatureSettings.HistoriaBase"/>
    public string? HistoriaBase { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.DevolucionTotalHoras"/>
    public int? DevolucionTotalHoras { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.DevolucionParcialHoras"/>
    public int? DevolucionParcialHoras { get; init; }

    /// <inheritdoc cref="EhrFeatureSettings.DevolucionParcialPorcentaje"/>
    public int? DevolucionParcialPorcentaje { get; init; }
}
