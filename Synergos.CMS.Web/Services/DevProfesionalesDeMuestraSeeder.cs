using System.Text.Json;
using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Notifications;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>Lo que dejó la siembra de los profesionales de muestra.</summary>
public sealed record SiembraDeProfesionalesDeMuestra(int Creados, int Actualizados, string? Motivo = null);

/// <summary>
/// Los profesionales de MUESTRA como contenido, para las demos de la copia piloto (003-salud-cita, I1 de producción, T052):
/// los cuatro de la muestra de desarrollo como <c>professionalPage</c> bajo el sitio de Salud, publicados, y rotulados
/// «(muestra)» en el nombre. Publicarlos dispara la oferta con su agenda (T047), así que la cita se puede agendar con
/// ellos de punta a punta.
/// </summary>
/// <remarks>
/// <para><b>Sólo a pedido</b>, por <c>POST /dev/seed-profesionales-de-muestra</c> y detrás de
/// <c>Synergos:DevSeed:Enabled</c> (ADR 0013): nada se siembra al arrancar, y en producción el endpoint no existe.</para>
///
/// <para><b>No son los del primer día</b>: los reales los da el arquitecto (Q2) y entran por el backoffice o por un lote
/// de contenido. Por eso el nombre lleva «(muestra)»: en una pantalla, nadie los confunde con un médico de verdad.</para>
///
/// <para><b>Idempotente</b>: un profesional que ya está (por su slug) se actualiza y se vuelve a publicar; no se
/// duplica. Sin foto, a propósito: no hay retrato de muestra que no sea un relleno.</para>
/// </remarks>
public sealed class DevProfesionalesDeMuestraSeeder
{
    /// <summary>Lo que lleva el nombre de cada uno: que se vea que es de muestra.</summary>
    public const string Rotulo = " (muestra)";

    private const string Culture = "es-CO";

    /// <summary>El valor que guarda el selector de días (<c>DTSelectWorkingDays</c>) para cada día.</summary>
    private static readonly IReadOnlyDictionary<DayOfWeek, string> Dias = new Dictionary<DayOfWeek, string>
    {
        [DayOfWeek.Monday] = "lunes",
        [DayOfWeek.Tuesday] = "martes",
        [DayOfWeek.Wednesday] = "miercoles",
        [DayOfWeek.Thursday] = "jueves",
        [DayOfWeek.Friday] = "viernes",
        [DayOfWeek.Saturday] = "sabado",
        [DayOfWeek.Sunday] = "domingo",
    };

    private readonly IContentService _contenidos;
    private readonly ILogger<DevProfesionalesDeMuestraSeeder> _log;

    public DevProfesionalesDeMuestraSeeder(IContentService contenidos, ILogger<DevProfesionalesDeMuestraSeeder> log)
    {
        _contenidos = contenidos;
        _log = log;
    }

    /// <summary>Crea o actualiza, y publica, los profesionales de muestra bajo el sitio de Salud.</summary>
    public async Task<SiembraDeProfesionalesDeMuestra> SembrarAsync(CancellationToken cancellationToken = default)
    {
        var sitio = SitioDeSalud();
        if (sitio is null)
        {
            return new SiembraDeProfesionalesDeMuestra(0, 0, "No hay un siteRoot con brandKey «healthcare».");
        }

        var existentes = Profesionales(sitio.Id);
        var (creados, actualizados) = (0, 0);
        foreach (var p in await new SaludDemoDirectorySource().GetAllAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var nodo = existentes.GetValueOrDefault(p.Id);
            if (nodo is null)
            {
                nodo = _contenidos.Create(p.FullName + Rotulo, sitio.Id, UmbracoProfessionalDirectorySource.ProfessionalPageAlias);
                creados++;
            }
            else
            {
                actualizados++;
            }

            Escribir(nodo, p);
            var publicado = _contenidos.SaveAndPublish(nodo, [Culture]);
            if (!publicado.Success)
            {
                _log.LogWarning("No se pudo publicar el profesional de muestra {Profesional}: {Resultado}.", p.Id, publicado.Result);
            }
        }

        return new SiembraDeProfesionalesDeMuestra(creados, actualizados);
    }

    private static void Escribir(IContent nodo, MedicalDoctor p)
    {
        nodo.SetCultureName(p.FullName + Rotulo, Culture);
        nodo.SetValue(OfertaDeProfesionalAlPublicar.PropiedadDelProfesional, p.Id);
        nodo.SetValue("professionalName", p.FullName + Rotulo, Culture);
        nodo.SetValue("professionalSpecialty", p.Specialty);
        nodo.SetValue("professionalLicense", p.LicenseNumber);
        nodo.SetValue("professionalYearsExperience", p.YearsExperience);
        nodo.SetValue("professionalPhone", p.Phone);
        nodo.SetValue("professionalEmail", p.Email);
        // DropDown.Flexible guarda un arreglo JSON, aunque sea de un solo valor.
        nodo.SetValue("professionalWorkingDays", JsonSerializer.Serialize(p.WorkingDays.Select(d => Dias[d])));
        nodo.SetValue("professionalSlotStartHour", p.SlotStartHour);
        nodo.SetValue("professionalSlotEndHour", p.SlotEndHour);
        nodo.SetValue("professionalSlotMinutes", p.SlotMinutes);
    }

    private IContent? SitioDeSalud()
    {
        var plataforma = _contenidos.GetRootContent().FirstOrDefault(c => c.ContentType.Alias == "platformRoot");
        return plataforma is null
            ? null
            : _contenidos.GetPagedChildren(plataforma.Id, 0, 200, out _)
                .FirstOrDefault(c => c.ContentType.Alias == "siteRoot"
                    && string.Equals(c.GetValue<string>("brandKey"), "healthcare", StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, IContent> Profesionales(int sitio)
    {
        var porSlug = new Dictionary<string, IContent>(StringComparer.Ordinal);
        foreach (var hijo in _contenidos.GetPagedChildren(sitio, 0, 500, out _)
            .Where(c => c.ContentType.Alias == UmbracoProfessionalDirectorySource.ProfessionalPageAlias))
        {
            var slug = hijo.GetValue<string>(OfertaDeProfesionalAlPublicar.PropiedadDelProfesional)?.Trim();
            if (!string.IsNullOrEmpty(slug)) porSlug.TryAdd(slug, hijo);
        }

        return porSlug;
    }
}
