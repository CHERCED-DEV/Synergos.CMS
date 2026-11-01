namespace Synergos.CMS.Interfaces.SynHost;

/// <summary>
/// Dónde vive una vista de una app en el sitio: la página que coloca su bloque con esa <c>vista</c>
/// (014-despiece-tienda, I3).
/// </summary>
/// <remarks>
/// La <c>Vista</c> viaja con el vocabulario del desplegable del bloque (<c>catalogo</c>, <c>carrito</c>…), y la
/// <c>Url</c> es la de la página, ya resuelta por Umbraco: el elemento enlaza ahí en vez de cambiar de vista dentro
/// de la página.
/// </remarks>
public sealed record RutaDeUnaVista(string Vista, string Url);
