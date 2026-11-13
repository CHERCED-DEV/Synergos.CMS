using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Catalog;
using Synergos.CMS.Web.Services.Salud;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar un <c>professionalPage</c>, publica su oferta en el orquestador de Salud con su agenda (003-salud-cita,
/// T047, sobre NN-22): un profesional nuevo queda agendable por la puerta sin tocar código ni el manifiesto. El molde de
/// <see cref="OfertaDeCursoAlPublicar"/>.
/// </summary>
/// <remarks>
/// <para><b>Sólo con el directorio en el contenido</b> (<c>Synergos:Catalog:Sources:Salud = cms</c>): en <c>demo</c> los
/// profesionales son la muestra y un <c>professionalPage</c> no se agenda.</para>
///
/// <para><b>Lee al profesional por el directorio y no del contenido que llega</b>: la agenda sale de la MISMA proyección
/// que pinta el portal (<see cref="ProfessionalContentRules"/>). Si el directorio no lo ve —todavía no está en la caché,
/// como en un import, o es de otro sitio— no se toca su oferta: queda un aviso y se repara republicando. Sin días de
/// atención no hay agenda que publicar, y tampoco se toca.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre. Lo que falle queda en el log, y se
/// repara republicando.</para>
/// </remarks>
public sealed class OfertaDeProfesionalAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    internal const string PropiedadDelProfesional = "professionalSlug";

    private readonly IDoctorDirectory _directorio;
    private readonly IPublicadorDeOfertasDeProfesionales _ofertas;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeProfesionalAlPublicar> _log;

    public OfertaDeProfesionalAlPublicar(
        IDoctorDirectory directorio,
        IPublicadorDeOfertasDeProfesionales ofertas,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeProfesionalAlPublicar> log)
    {
        _directorio = directorio;
        _ofertas = ofertas;
        _fuentes = fuentes;
        _log = log;
    }

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var fichas = notification.PublishedEntities
            .Where(c => string.Equals(c.ContentType.Alias, UmbracoProfessionalDirectorySource.ProfessionalPageAlias, StringComparison.Ordinal))
            .ToList();
        if (fichas.Count == 0 || !DelContenido(_fuentes)) return;

        foreach (var ficha in fichas)
        {
            var id = ficha.GetValue<string>(PropiedadDelProfesional)?.Trim();
            if (string.IsNullOrEmpty(id)) continue;

            try
            {
                var profesional = await _directorio.GetAsync(id, cancellationToken).ConfigureAwait(false);
                if (profesional is null)
                {
                    _log.LogWarning(
                        "Se publicó el profesional {Profesional} y el directorio no lo ve (todavía no está en la caché, como en "
                        + "un import, o es de otro sitio): no se toca su oferta. Si es de Salud, se repara republicando.", id);
                    continue;
                }

                if (profesional.WorkingDays.Count == 0)
                {
                    _log.LogWarning(
                        "Se publicó el profesional {Profesional} sin días de atención: no hay agenda que publicar y no se toca "
                        + "su oferta. Se repara marcando sus días y republicando.", id);
                    continue;
                }

                await _ofertas.PublicarAsync(profesional, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del profesional {Profesional}. Se repara republicando.", id);
            }
        }
    }

    /// <summary>Si los profesionales salen del contenido: sólo entonces un <c>professionalPage</c> se agenda por la puerta.</summary>
    internal static bool DelContenido(IOptionsMonitor<CatalogSettings> fuentes)
        => fuentes.CurrentValue.Sources.TryGetValue(UmbracoProfessionalDirectorySource.Vertical, out var fuente)
           && string.Equals(fuente, "cms", StringComparison.OrdinalIgnoreCase);
}
