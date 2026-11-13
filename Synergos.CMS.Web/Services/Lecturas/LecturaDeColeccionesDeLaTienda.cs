using Synergos.CMS.Application.Services.Impl;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Web.Services.Lecturas;

/// <summary>
/// Una colección del miembro en la tienda (la lista de deseos) en la lectura genérica (014-despiece-tienda, I4), con
/// cada producto leído del catálogo de la tienda, el mismo del listado. <c>GET api/shop/wishlist</c>, que sigue como
/// interina, los leía de otra fuente (<see cref="IShopQuery"/>). Es la colección genérica con la clave de la tienda
/// (012-blogs-social, I4).
/// </summary>
public sealed class LecturaDeColeccionesDeLaTienda : LecturaDeColeccionesDelVertical
{
    /// <summary>Sobre las colecciones del miembro y el catálogo de la tienda.</summary>
    public LecturaDeColeccionesDeLaTienda(IUserCollection colecciones, IEnumerable<ILecturaDelCatalogo> catalogos)
        : base(CatalogoDeLaTienda.Vertical, colecciones, catalogos)
    {
    }
}
