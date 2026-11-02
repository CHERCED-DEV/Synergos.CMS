namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Un filtro fijo del catálogo que declara el bloque (014-despiece-tienda, I5): el campo de una faceta del listado
/// genérico y el valor que se fija. El elemento lo aplica en cada búsqueda y no lo ofrece como filtro: la página
/// «Ofertas» es el catálogo con <c>oferta = En oferta</c>.
/// </summary>
public sealed record FiltroFijo(string Campo, string Valor);
