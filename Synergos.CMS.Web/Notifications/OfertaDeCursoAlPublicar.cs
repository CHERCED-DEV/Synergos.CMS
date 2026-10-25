using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Web.Services.Academy;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al publicar un <c>coursePage</c>, publica su oferta en el orquestador de Educación (010-educacion-matricula): un
/// curso nuevo queda matriculable por la puerta sin tocar código ni configuración. El molde de
/// <see cref="OfertaDeTiendaAlPublicar"/>.
/// </summary>
/// <remarks>
/// <para><b>Sólo con el catálogo en el contenido</b> (<c>Synergos:Catalog:Sources:Academy = cms</c>): en <c>demo</c> el
/// catálogo es la siembra y un <c>coursePage</c> no se matricula. La siembra se publica a mano, con
/// <c>POST /api/academy/oferta/republicar</c>.</para>
///
/// <para><b>Lee el curso por el catálogo y no del contenido que llega</b>: la oferta sale de la MISMA proyección que
/// pinta la ficha (el precio ya leído como precio, los planes de las reglas de precio). Un curso que el catálogo omite
/// (sin título, con un precio ilegible) no tiene ficha, y tampoco oferta: la que tuviera se RETIRA.</para>
///
/// <para><b>Antes de pedir el curso, pregunta si el catálogo ya lo ve en la academia</b>
/// (<see cref="IAcademiaEnElContenido"/>, sin log). Al publicar en lote una ficha puede publicarse antes de que el
/// catálogo alcance su <c>siteRoot</c>: es pasajero, y entonces no se retira nada; queda un aviso y se repara
/// republicando. Un curso de otro sitio tampoco se toca.</para>
///
/// <para><b>Nunca tumba el publicar</b>: el contenido ya se guardó cuando esto corre. Lo que falle queda en el log, y
/// se repara republicando.</para>
/// </remarks>
public sealed class OfertaDeCursoAlPublicar : INotificationAsyncHandler<ContentPublishedNotification>
{
    internal const string PropiedadDelCurso = "courseSlug";

    private readonly ICourseCatalogProvider _catalogo;
    private readonly IPublicadorDeOfertasDeCursos _ofertas;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly IAcademiaEnElContenido _academia;
    private readonly ILogger<OfertaDeCursoAlPublicar> _log;

    public OfertaDeCursoAlPublicar(
        ICourseCatalogProvider catalogo,
        IPublicadorDeOfertasDeCursos ofertas,
        IOptionsMonitor<CatalogSettings> fuentes,
        IAcademiaEnElContenido academia,
        ILogger<OfertaDeCursoAlPublicar> log)
    {
        _catalogo = catalogo;
        _ofertas = ofertas;
        _fuentes = fuentes;
        _academia = academia;
        _log = log;
    }

    public async Task HandleAsync(ContentPublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var cursos = notification.PublishedEntities
            .Where(c => string.Equals(c.ContentType.Alias, UmbracoCourseCatalogSource.CoursePageAlias, StringComparison.Ordinal))
            .ToList();
        if (cursos.Count == 0 || !DelContenido(_fuentes)) return;

        foreach (var contenido in cursos)
        {
            var id = contenido.GetValue<string>(PropiedadDelCurso)?.Trim();
            if (string.IsNullOrEmpty(id)) continue;

            if (!_academia.Ve(contenido.Key))
            {
                _log.LogWarning(
                    "Se publicó el curso {Curso} y el catálogo no lo ve en la academia (todavía no está en la caché, como en "
                    + "un import, o es de otro sitio): no se toca su oferta. Si es de la academia, se repara republicando "
                    + "(POST /api/academy/oferta/republicar).", id);
                continue;
            }

            try
            {
                var curso = await _catalogo.GetCourseAsync(id, cancellationToken).ConfigureAwait(false);
                if (curso is null)
                {
                    _log.LogWarning(
                        "Se publicó el curso {Curso} y el catálogo no lo sirve: se retira su oferta. Ver el log del catálogo.", id);
                    await _ofertas.RetirarAsync(id, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await _ofertas.PublicarAsync(curso, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo publicar la oferta del curso {Curso}. Se repara republicando.", id);
            }
        }
    }

    /// <summary>Si los cursos salen del contenido: sólo entonces un <c>coursePage</c> se matricula por la puerta.</summary>
    internal static bool DelContenido(IOptionsMonitor<CatalogSettings> fuentes)
        => fuentes.CurrentValue.Sources.TryGetValue(UmbracoCourseCatalogSource.Vertical, out var fuente)
           && string.Equals(fuente, "cms", StringComparison.OrdinalIgnoreCase);
}
