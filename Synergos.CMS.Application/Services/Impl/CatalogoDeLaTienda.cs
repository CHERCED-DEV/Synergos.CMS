using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Interfaces;
using Synergos.CMS.Interfaces.Lecturas;

namespace Synergos.CMS.Application.Services.Impl;

/// <summary>
/// La tienda en las lecturas genéricas (014-despiece-tienda, I4): su clave de vertical y su catálogo como lectura
/// neutra, sobre la MISMA fuente de productos que <see cref="StubProductCatalogProvider"/> y con el mismo índice
/// (búsqueda, facetas y órdenes), así que el listado genérico y el de <c>api/shop/search</c> dan lo mismo.
/// </summary>
public static class CatalogoDeLaTienda
{
    /// <summary>La clave de la tienda en <c>api/lecturas/{vertical}</c>.</summary>
    public const string Vertical = "tienda";

    /// <summary>El catálogo de la tienda como lectura genérica.</summary>
    public static ILecturaDelCatalogo Lectura(
        ICatalogSource<CatalogProduct> fuente,
        CatalogSettings ajustes,
        IPriceFormatter precios)
    {
        ArgumentNullException.ThrowIfNull(ajustes);
        return new LecturaDelCatalogo<CatalogProduct>(
            Vertical,
            fuente,
            new InMemoryCatalogIndex<CatalogProduct>(StubProductCatalogProvider.Descriptor, ajustes),
            Proyeccion(precios),
            ajustes.MaxTake);
    }

    /// <summary>Un producto en la forma neutra: la tarjeta con marca y categoría, y la ficha con variantes y preguntas.</summary>
    internal static ProyeccionDelCatalogo<CatalogProduct> Proyeccion(IPriceFormatter precios)
    {
        ArgumentNullException.ThrowIfNull(precios);
        return new ProyeccionDelCatalogo<CatalogProduct>(
            Id: p => p.Id,
            Elemento: p => Elemento(p, precios),
            Ficha: p => new FichaDelCatalogo(
                Elemento: Elemento(p, precios),
                Descripcion: p.Description,
                Imagenes: p.Images,
                Opciones: p.Variants
                    .Select(v => new OpcionDelElemento(v.VariantId, v.Name, PrecioDe(v.Price, precios), v.Stock))
                    .ToList(),
                Preguntas: p.Questions
                    .Select(q => new PreguntaDelElemento(q.Asker, q.Question, q.Answer, q.Date))
                    .ToList()));
    }

    private static ElementoDelCatalogo Elemento(CatalogProduct p, IPriceFormatter precios)
    {
        var atributos = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(p.Brand))
        {
            atributos["marca"] = p.Brand;
        }

        if (!string.IsNullOrWhiteSpace(p.Category))
        {
            atributos["categoria"] = p.Category;
        }

        return new ElementoDelCatalogo(
            Id: p.Id,
            Titulo: p.Name,
            Imagen: string.IsNullOrWhiteSpace(p.ImageUrl) ? null : p.ImageUrl,
            Precio: PrecioDe(p.Price, precios),
            Calificacion: p.Reviews.Count == 0 && p.Rating <= 0 ? null : new Calificacion(p.Rating, p.Reviews.Count),
            Disponibles: p.TotalStock,
            Atributos: atributos);
    }

    private static Precio PrecioDe(decimal monto, IPriceFormatter precios)
        => new(monto, ShopDemoSeed.Currency, precios.Format(monto, ShopDemoSeed.Currency));
}
