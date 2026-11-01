using Synergos.CMS.Application.Services.Impl;

namespace Synergos.CMS.Application.Configuration;

/// <summary>
/// La base de las lecturas genéricas de la tienda (014-despiece-tienda, I4), la misma para la tienda
/// (<see cref="StorefrontFeatureSettings"/>) y para la consola (<see cref="SellerFeatureSettings"/>).
/// </summary>
public static class LecturasDeLaTienda
{
    /// <summary>Por defecto: las de la tienda en <c>api/lecturas/{vertical}</c>.</summary>
    public const string Base = "/api/lecturas/" + CatalogoDeLaTienda.Vertical;

    /// <summary>
    /// Qué está mal en la base declarada, o <c>null</c>. Vacía vale: la tienda vuelve a las rutas interinas.
    /// </summary>
    public static string? Problema(string clave, string? valor)
        => string.IsNullOrWhiteSpace(valor) ? null : ReglasDeNegocio.ApiBase(clave, valor);
}
