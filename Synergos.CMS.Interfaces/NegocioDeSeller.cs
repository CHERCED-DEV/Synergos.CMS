namespace Synergos.CMS.Interfaces;

/// <summary>Los valores de negocio de la consola del vendedor ya fusionados para un sitio (ADR 0137, escala #196).</summary>
/// <param name="ApiBase">Dónde vive la API de la funcionalidad (relativa al sitio o absoluta).</param>
/// <param name="LecturasBase">La base de las lecturas genéricas de la tienda (014-despiece-tienda, I4); vacía, la
/// consola lee las rutas interinas de <paramref name="ApiBase"/>.</param>
public sealed record NegocioDeSeller(string ApiBase, string LecturasBase = "");
