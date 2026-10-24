using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Services.Academy;
using Synergos.CMS.Web.Services.Catalog;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Notifications;

/// <summary>
/// Al despublicar, mandar a la papelera o borrar un <c>coursePage</c> —o un nodo que lo contiene—, retira su oferta del
/// orquestador de Educación: el curso deja de matricularse por la puerta (010-educacion-matricula). El molde de
/// <see cref="OfertaDeTiendaAlRetirar"/>.
/// </summary>
/// <remarks>
/// <para><b>Por qué hace falta.</b> La matrícula por la puerta cotiza contra los precios que el orquestador publicó, no
/// contra la ficha: un curso despublicado se seguiría matriculando. Una matrícula ya abierta se cierra igual, con el
/// total que se le cotizó; abrir otra se rechaza.</para>
///
/// <para><b>Con sus descendientes</b> al despublicar o mandar a la papelera; al borrar ya no hay descendientes que
/// leer. <b>Sólo con el catálogo en el contenido</b>, como publicar, y <b>nunca tumba</b> la acción del editor: lo que
/// falle queda en el log y se repara retirándolo otra vez.</para>
/// </remarks>
public sealed class OfertaDeCursoAlRetirar :
    INotificationAsyncHandler<ContentUnpublishedNotification>,
    INotificationAsyncHandler<ContentMovedToRecycleBinNotification>,
    INotificationAsyncHandler<ContentDeletedNotification>
{
    private const int Pagina = 500;

    private readonly IPublicadorDeOfertasDeCursos _ofertas;
    private readonly IContentService _contenidos;
    private readonly IOptionsMonitor<CatalogSettings> _fuentes;
    private readonly ILogger<OfertaDeCursoAlRetirar> _log;

    public OfertaDeCursoAlRetirar(
        IPublicadorDeOfertasDeCursos ofertas,
        IContentService contenidos,
        IOptionsMonitor<CatalogSettings> fuentes,
        ILogger<OfertaDeCursoAlRetirar> log)
    {
        _ofertas = ofertas;
        _contenidos = contenidos;
        _fuentes = fuentes;
        _log = log;
    }

    public Task HandleAsync(ContentUnpublishedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.UnpublishedEntities, conDescendientes: true, cancellationToken);
    }

    public Task HandleAsync(ContentMovedToRecycleBinNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.MoveInfoCollection.Select(m => m.Entity), conDescendientes: true, cancellationToken);
    }

    public Task HandleAsync(ContentDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        return RetirarAsync(notification.DeletedEntities, conDescendientes: false, cancellationToken);
    }

    private async Task RetirarAsync(IEnumerable<IContent> nodos, bool conDescendientes, CancellationToken ct)
    {
        if (!OfertaDeCursoAlPublicar.DelContenido(_fuentes)) return;

        var cursos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nodo in nodos)
        {
            Anotar(nodo, cursos);
            if (conDescendientes) Descendientes(nodo, cursos);
        }

        foreach (var curso in cursos)
        {
            try
            {
                await _ofertas.RetirarAsync(curso, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning(ex, "No se pudo retirar la oferta del curso {Curso}. Se repara retirándolo otra vez.", curso);
            }
        }
    }

    private void Descendientes(IContent nodo, HashSet<string> cursos)
    {
        try
        {
            long total;
            var pagina = 0L;
            do
            {
                foreach (var hijo in _contenidos.GetPagedDescendants(nodo.Id, pagina++, Pagina, out total)) Anotar(hijo, cursos);
            }
            while (pagina * Pagina < total);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "No se pudieron leer los descendientes de {Nodo} para retirar sus ofertas de cursos.", nodo.Id);
        }
    }

    private static void Anotar(IContent nodo, HashSet<string> cursos)
    {
        if (!string.Equals(nodo.ContentType.Alias, UmbracoCourseCatalogSource.CoursePageAlias, StringComparison.Ordinal)) return;
        var curso = nodo.GetValue<string>(OfertaDeCursoAlPublicar.PropiedadDelCurso)?.Trim();
        if (!string.IsNullOrEmpty(curso)) cursos.Add(curso);
    }
}
