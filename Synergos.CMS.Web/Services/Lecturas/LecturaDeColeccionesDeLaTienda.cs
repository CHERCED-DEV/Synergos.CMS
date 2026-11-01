using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Una colección del miembro en la tienda (la lista de deseos) en la lectura genérica (014-despiece-tienda, I4), con
/// cada producto leído del catálogo de la tienda, el mismo del listado. <c>GET api/shop/wishlist</c>, que sigue como
/// interina, los leía de otra fuente (<see cref="IShopQuery"/>).
/// </summary>
public sealed class LecturaDeColeccionesDeLaTienda : ILecturaDeColecciones
{
    private readonly IUserCollection _colecciones;
    private readonly IEnumerable<ILecturaDelCatalogo> _catalogos;

    /// <summary>Sobre las colecciones del miembro y el catálogo de la tienda.</summary>
    public LecturaDeColeccionesDeLaTienda(IUserCollection colecciones, IEnumerable<ILecturaDelCatalogo> catalogos)
    {
        _colecciones = colecciones ?? throw new ArgumentNullException(nameof(colecciones));
        _catalogos = catalogos ?? throw new ArgumentNullException(nameof(catalogos));
    }

    /// <inheritdoc />
    public string Vertical => CatalogoDeLaTienda.Vertical;

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
