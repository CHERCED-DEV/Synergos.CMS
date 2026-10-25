using Microsoft.Extensions.Options;
using Synergos.CMS.Application.Configuration;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Catalog;

/// <summary>Si el catálogo del contenido ya ve un curso publicado dentro de la academia.</summary>
public interface IAcademiaEnElContenido
{
    /// <summary>
    /// Si el catálogo, por su propio recorrido de la caché publicada, encuentra el <c>siteRoot</c> de la academia (el
    /// <c>brandKey</c> de <c>Synergos:Catalog:Scopes:Academy</c>) y, entre sus cursos, el nodo <paramref name="nodo"/>.
    /// Nunca deja log.
    /// </summary>
    bool Ve(Guid nodo);
}

/// <summary>
/// La pregunta «¿el catálogo ve ya este curso en la academia?», sin log (010-educacion-matricula). El molde de
/// <see cref="TiendaEnElContenido"/>.
/// </summary>
/// <remarks>
/// <para><b>Para quien no es una página.</b> Al publicar cursos en lote —un import del árbol entero, una restauración,
/// la caché sin calentar— cada <c>coursePage</c> puede publicarse antes de que el recorrido desde las raíces alcance su
/// <c>siteRoot</c>. Es pasajero: preguntar al catálogo ahí dejaría un ERR por curso y el manejador de la oferta la
/// RETIRARÍA, y la academia quedaría sin matricular por la puerta hasta republicar. Es el rojo que la tienda ya pagó
/// (004, 2026-10-22).</para>
///
/// <para><b>El mismo recorrido que el catálogo</b> (<see cref="UmbracoCourseCatalogSource.BuscarAcademia"/> y
/// <see cref="UmbracoCourseCatalogSource.CursosDe"/>), y sin logger a propósito: quien pregunta decide qué decir.</para>
/// </remarks>
public sealed class AcademiaEnElContenido : IAcademiaEnElContenido
{
    private readonly IUmbracoContextAccessor _contexto;
    private readonly IOptionsMonitor<CatalogSettings> _ajustes;
    private readonly IVariationContextAccessor _variacion;

    public AcademiaEnElContenido(
        IUmbracoContextAccessor contexto,
        IOptionsMonitor<CatalogSettings> ajustes,
        IVariationContextAccessor variacion)
    {
        _contexto = contexto;
        _ajustes = ajustes;
        _variacion = variacion;
    }

    /// <inheritdoc />
    public bool Ve(Guid nodo)
    {
        var marca = UmbracoCourseCatalogSource.MarcaDeLaAcademia(_ajustes.CurrentValue);
        if (marca is null || !_contexto.TryGetUmbracoContext(out var contexto) || contexto.Content is not { } cache)
        {
            return false;
        }

        var academia = UmbracoCourseCatalogSource.BuscarAcademia(cache, marca, _variacion);
        return academia is not null
            && UmbracoCourseCatalogSource.CursosDe(academia, _variacion).Any(curso => curso.Key == nodo);
    }
}
