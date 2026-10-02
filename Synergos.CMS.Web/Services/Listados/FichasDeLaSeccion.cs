using Synergos.CMS.Interfaces.SynHost;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;

namespace Synergos.CMS.Web.Services.Listados;

/// <summary>
/// Las fichas (<c>productPage</c>) de la sección de la página: las que cuelgan del padre de la
/// página que coloca el listado. «Servicios» bajo «Booking» lista las fichas de Booking, cada una
/// con su página.
/// </summary>
/// <remarks>
/// <para>El precio es <c>productPriceBase</c>, un texto libre: se lee con
/// <see cref="PrecioAutorado"/>, el único sitio que decide si un texto es un precio. Una ficha sin
/// precio inequívoco sale sin el dato, no con un cero.</para>
///
/// <para>La moneda es la del catálogo, como en las demás fuentes del CMS
/// (<c>UmbracoCourseCatalogSource</c>, <c>UmbracoEventCatalogSource</c>…): un despliegue es un
/// origen y todo el catálogo emite pesos.</para>
/// </remarks>
public sealed class FichasDeLaSeccion : IFuenteDeListado
{
    private const string FichaAlias = "productPage";
    private const string CategoriaAlias = "productCategoryPage";
    private const string Moneda = "COP";

    private readonly IUmbracoContextAccessor _contexto;

    public FichasDeLaSeccion(IUmbracoContextAccessor contexto) => _contexto = contexto;

    public string Clave => "fichas";

    public IReadOnlyList<FilaDelListado> Filas(PeticionDelListado peticion)
    {
        ArgumentNullException.ThrowIfNull(peticion);
        if (!_contexto.TryGetUmbracoContext(out var contexto)
            || contexto.PublishedRequest?.PublishedContent is not { } pagina)
        {
            return [];
        }

        var seccion = pagina.Parent ?? pagina;
        return seccion.DescendantsOfType(FichaAlias)
            .Select(ficha => (Ficha: ficha, Nombre: ficha.Value<string>("productName") is { Length: > 0 } n ? n.Trim() : ficha.Name ?? string.Empty,
                Categoria: ficha.Parent?.ContentType.Alias == CategoriaAlias ? ficha.Parent.Name : null))
            .Where(f => FormatoDelListado.Responde(peticion.Consulta, f.Nombre, f.Categoria, f.Ficha.Value<string>("productSubtitle")))
            .Select(f => new FilaDelListado(
                Id: f.Ficha.Key.ToString(),
                Title: f.Nombre,
                Href: f.Ficha.Url(),
                Image: MediaPickerReader.ReadFirstMediaUrl(f.Ficha, "productImages"),
                ImageAlt: f.Nombre,
                Badge: f.Categoria,
                Specs: Datos(f.Ficha, peticion)))
            .ToList();
    }

    private static IReadOnlyList<DatoDeLaFila>? Datos(IPublishedContent ficha, PeticionDelListado peticion)
    {
        var datos = new List<DatoDeLaFila>();
        if (ficha.Value<string>("productSubtitle") is { Length: > 0 } detalle)
        {
            datos.Add(new(peticion.Rotulo("DataGrid.Detail", "Detalle"), detalle.Trim()));
        }

        if (PrecioAutorado.EsInequivoco(ficha.Value<string>("productPriceBase")?.Trim(), out var precio) && precio > 0m)
        {
            datos.Add(new(peticion.Rotulo("DataGrid.Price", "Precio"), FormatoDelListado.Precio(precio, Moneda, peticion.Cultura)));
        }

        return datos.Count > 0 ? datos : null;
    }
}
