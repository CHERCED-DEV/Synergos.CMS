using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Una colección del miembro en la lectura genérica, con cada elemento leído del catálogo de SU vertical
/// (014-despiece-tienda, I4; genérica en 012-blogs-social, I4): la lista de deseos de la tienda, los guardados de
/// Blogs. La colección es de <see cref="IUserCollection"/>, que ya era común; sólo cambia la clave del vertical.
/// </summary>
/// <remarks>
/// Lo que el catálogo ya no tiene sale con su id y sin elemento: se muestra que estaba, no se inventa.
/// </remarks>
public class LecturaDeColeccionesDelVertical : ILecturaDeColecciones
{
    private readonly IUserCollection _colecciones;
    private readonly IEnumerable<ILecturaDelCatalogo> _catalogos;

    /// <summary>Las colecciones del miembro en <paramref name="vertical"/>, hidratadas con el catálogo de ese vertical.</summary>
    public LecturaDeColeccionesDelVertical(
        string vertical,
        IUserCollection colecciones,
        IEnumerable<ILecturaDelCatalogo> catalogos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vertical);
        Vertical = vertical.Trim().ToLowerInvariant();
        _colecciones = colecciones ?? throw new ArgumentNullException(nameof(colecciones));
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
    }

    /// <inheritdoc />
    public string Vertical { get; }

    /// <inheritdoc />
    public async Task<ColeccionDelMiembro> ColeccionAsync(
        string correo,
        string coleccion,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correo);
        ArgumentException.ThrowIfNullOrWhiteSpace(coleccion);

        var guardados = await _colecciones.GetAsync(correo, coleccion, cancellationToken).ConfigureAwait(false);
        var catalogo = _catalogos.DelVertical(Vertical);
        var elementos = catalogo is null
            ? []
            : await catalogo.ElementosAsync(guardados.Select(g => g.ItemRef).ToList(), cancellationToken).ConfigureAwait(false);

        var porId = new Dictionary<string, ElementoDelCatalogo>(StringComparer.OrdinalIgnoreCase);
        foreach (var elemento in elementos)
        {
            porId.TryAdd(elemento.Id, elemento);
        }

        return new ColeccionDelMiembro(
            Coleccion: coleccion,
            Elementos: guardados
                .Select(g => new ElementoGuardado(g.ItemRef, g.AddedAt, porId.GetValueOrDefault(g.ItemRef)))
                .ToList());
    }
}
