using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Compone la tienda de la demo por páginas (014-despiece-tienda, I3, T016): una página por vista, cada una con el
/// bloque de su app y su <c>vista</c>, dentro de la misma sección con que la Tienda coloca la suya.
/// </summary>
/// <remarks>
/// <para><b>Herramienta, no siembra</b> (ADR 0013): no corre sola; se invoca con <c>POST /dev/componer-la-tienda</c>
/// detrás de <c>Synergos:DevSeed:Enabled</c>. Lo que crea se exporta con uSync (ADR 0129) y al repo va ese export: en
/// el servidor, las páginas llegan con el import, no con esta herramienta.</para>
///
/// <para><b>Idempotente y no destructiva</b>: una página que ya existe con su nombre bajo su padre no se toca, así que
/// repetirla no pisa lo que el editor haya cambiado después.</para>
///
/// <para>Las páginas no salen en el menú del sitio (<c>hideFromMainMenu</c>): la tienda tiene su propia barra, que
/// enlaza cada una. El editor las puede sacar al menú cuando quiera.</para>
/// </remarks>
public sealed class DevTiendaPorPaginas
{
    private const string Cultura = "es-CO";
    private const string Rejilla = "sections";
    private const string Seccion = "elementLayoutSection";

    /// <summary>El área de contenido de <c>elementLayoutSection</c> en la rejilla de secciones (<c>DTBlockGridSections</c>).</summary>
    private static readonly Guid AreaDeLaSeccion = Guid.Parse("3525d41c-ae84-47ac-9297-2148f6a4aae8");

    /// <summary>La Tienda de la demo (<c>/SynergosLabs/Tienda</c>), por su Key de uSync.</summary>
    public static readonly Guid Tienda = Guid.Parse("cd460fe9-fac2-4d01-b6c7-5c23c4b7c38b");

    /// <summary>La página «Vender» de la Tienda, la de la consola, por su Key de uSync.</summary>
    public static readonly Guid Vender = Guid.Parse("13dda046-015b-4639-b6cc-bdf37329b19d");

    /// <summary>Una página por componer: su nombre, bajo qué página cuelga, qué app coloca y con qué vista.</summary>
    public sealed record PaginaDeUnaVista(string Nombre, Guid Padre, string Bloque, string Vista, string ClaseDeLaSeccion);

    /// <summary>
    /// Las páginas, en el orden en que se crean. El inicio de la tienda (la Tienda) y el panel de la consola (Vender) ya
    /// existen: su bloque, sin vista, es el inicio y el panel.
    /// </summary>
    public static readonly IReadOnlyList<PaginaDeUnaVista> Paginas =
    [
        new("Catálogo", Tienda, "elementSynStorefront", "catalogo", "syn-storefront__module"),
        new("Producto", Tienda, "elementSynStorefront", "ficha", "syn-storefront__module"),
        new("Carrito", Tienda, "elementSynStorefront", "carrito", "syn-storefront__module"),
        new("Pago", Tienda, "elementSynStorefront", "pago", "syn-storefront__module"),
        new("Confirmación", Tienda, "elementSynStorefront", "confirmacion", "syn-storefront__module"),
        new("Mi cuenta", Tienda, "elementSynStorefront", "cuenta", "syn-storefront__module"),
        // «Escritorio» a secas ya es una categoría de la Tienda: dos nodos con el mismo nombre se pisan el fichero de uSync.
        new("Escritorio del vendedor", Vender, "elementSynStorefront", "vendedor", "syn-storefront__module"),
        new("Publicar", Vender, "elementSynSeller", "publicar", "syn-seller__module"),
        new("Mensajes", Vender, "elementSynSeller", "mensajes", "syn-seller__module"),
    ];

    private readonly IContentService _contenido;
    private readonly IContentTypeService _tipos;
    private readonly SchemaBlockDefaults _defectos;

    public DevTiendaPorPaginas(IContentService contenido, IContentTypeService tipos, SchemaBlockDefaults defectos)
    {
        _contenido = contenido;
        _tipos = tipos;
        _defectos = defectos;
    }

    /// <summary>Lo que hizo con cada página, en orden; si algo faltó, para ahí y lo dice.</summary>
    public sealed record ResultadoDeLaComposicion(bool Ok, IReadOnlyList<string> Paginas, string? Error);

    public ResultadoDeLaComposicion Componer()
    {
        var hechas = new List<string>();
        var seccion = _tipos.Get(Seccion);
        if (seccion is null)
        {
            return new ResultadoDeLaComposicion(false, hechas, $"falta el tipo {Seccion}");
        }

        foreach (var pagina in Paginas)
        {
            var padre = _contenido.GetById(pagina.Padre);
            var app = _tipos.Get(pagina.Bloque);
            if (padre is null || app is null)
            {
                return new ResultadoDeLaComposicion(false, hechas, $"«{pagina.Nombre}»: falta {(padre is null ? $"la página {pagina.Padre}" : $"el tipo {pagina.Bloque}")}");
            }

            var existente = _contenido.GetPagedChildren(padre.Id, 0, 500, out _)
                .FirstOrDefault(hija => hija.GetCultureName(Cultura) == pagina.Nombre || hija.Name == pagina.Nombre);
            if (existente is not null)
            {
                hechas.Add($"{pagina.Nombre}: ya estaba (id {existente.Id})");
                continue;
            }

            // Un desplegable dentro de una rejilla se guarda como el texto de un arreglo JSON (`["none"]`), igual que
            // lo guarda el backoffice: el export de uSync lo enseña expandido, y LayoutPresetDefaults lo lee como texto.
            var rejilla = new BlockGridJsonBuilder();
            rejilla.AddTopLevelBlock(seccion.Key)
                .Set("cssClass", pagina.ClaseDeLaSeccion)
                .Set("spacingTop", "[\"none\"]")
                .Set("spacingBottom", "[\"none\"]")
                .Set("spacingInline", "[\"none\"]")
                .ApplyDefaults(_defectos.DefaultsFor(seccion.Key))
                .AddChild(AreaDeLaSeccion, app.Key, bloque => bloque
                    .Set("vista", $"[\"{pagina.Vista}\"]")
                    .ApplyDefaults(_defectos.DefaultsFor(app.Key)));

            var nueva = _contenido.Create(pagina.Nombre, padre.Id, "pageBase");
            // El contenido VARÍA POR CULTURA: sin el nombre de la cultura, guardar falla con «empty name».
            nueva.SetCultureName(pagina.Nombre, Cultura);
            nueva.SetValue(Rejilla, rejilla.Build(), Cultura);
            if (nueva.HasProperty("hideFromMainMenu"))
            {
                nueva.SetValue("hideFromMainMenu", 1);
            }

            // Sin el título de la página encima, como «Vender»: la barra de la app ya dice dónde se está.
            if (nueva.HasProperty("showTitle"))
            {
                nueva.SetValue("showTitle", 0);
            }

            var guardada = _contenido.SaveAndPublish(nueva, new[] { Cultura });
            if (!guardada.Success)
            {
                return new ResultadoDeLaComposicion(false, hechas, $"«{pagina.Nombre}»: no se publicó ({guardada.Result})");
            }

            hechas.Add($"{pagina.Nombre}: creada (id {nueva.Id}, vista {pagina.Vista})");
        }

        return new ResultadoDeLaComposicion(true, hechas, null);
    }
}
