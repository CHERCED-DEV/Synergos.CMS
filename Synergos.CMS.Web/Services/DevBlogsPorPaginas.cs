using Umbraco.Cms.Core.Services;

namespace Synergos.CMS.Web.Services;

/// <summary>
/// Compone la red social de la demo por páginas (012-blogs-social, I3, T021): una página por vista bajo Blogs, cada una
/// con el bloque de la app y su <c>vista</c>, dentro de la misma sección con que la raíz de Blogs coloca la suya.
/// </summary>
/// <remarks>
/// <para><b>Herramienta, no siembra</b> (ADR 0013), como la de la tienda (<see cref="DevTiendaPorPaginas"/>): no corre
/// sola; se invoca con <c>POST /dev/componer-blogs</c> detrás de <c>Synergos:DevSeed:Enabled</c>. Lo que crea se exporta
/// con uSync (ADR 0129) y al repo va ese export: en el servidor, las páginas llegan con el import, no con esta
/// herramienta.</para>
///
/// <para><b>Idempotente y no destructiva</b>: una página que ya existe con su nombre bajo Blogs no se toca, así que
/// repetirla no pisa lo que el editor haya cambiado después.</para>
///
/// <para>Las páginas no salen en el menú del sitio (<c>hideFromMainMenu</c>) ni llevan el título encima: la red tiene su
/// propio riel, que enlaza cada una. El editor las puede sacar al menú cuando quiera.</para>
/// </remarks>
public sealed class DevBlogsPorPaginas
{
    private const string Cultura = "es-CO";
    private const string Rejilla = "sections";
    private const string Seccion = "elementLayoutSection";
    private const string Bloque = "elementSynBlogs";
    private const string ClaseDeLaSeccion = "syn-blogs__module";

    /// <summary>El área de contenido de <c>elementLayoutSection</c> en la rejilla de secciones (<c>DTBlockGridSections</c>).</summary>
    private static readonly Guid AreaDeLaSeccion = Guid.Parse("3525d41c-ae84-47ac-9297-2148f6a4aae8");

    /// <summary>La raíz de Blogs de la demo (<c>/blogs/</c>), por su Key de uSync: ahí vive el inicio de la red.</summary>
    public static readonly Guid Blogs = Guid.Parse("0d857159-703a-410c-b9f5-a0ccc2c9b3eb");

    /// <summary>Una página por componer: su nombre y la vista de la red que muestra.</summary>
    public sealed record PaginaDeUnaVista(string Nombre, string Vista);

    /// <summary>
    /// Las páginas, en el orden en que se crean. El inicio de la red ya existe: es la raíz de Blogs, con su bloque sin
    /// vista. La suscripción no tiene página: está apagada (P5).
    /// </summary>
    public static readonly IReadOnlyList<PaginaDeUnaVista> Paginas =
    [
        new("Explorar", "explorar"),
        new("Publicación", "publicacion"),
        new("Perfil", "perfil"),
        new("Notificaciones", "notificaciones"),
        // «Mensajes» a secas ya es una página de la consola de la tienda (Vender): dos nodos con el mismo nombre se pisan
        // el fichero de uSync, y el export le cambia el nombre a uno de los dos según el orden (lo mismo que «Escritorio»
        // en 014).
        new("Mensajes directos", "mensajes"),
        new("Guardados", "guardados"),
        new("Escribir", "escribir"),
        new("Estudio", "estudio"),
    ];

    private readonly IContentService _contenido;
    private readonly IContentTypeService _tipos;
    private readonly SchemaBlockDefaults _defectos;

    public DevBlogsPorPaginas(IContentService contenido, IContentTypeService tipos, SchemaBlockDefaults defectos)
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
        var app = _tipos.Get(Bloque);
        var blogs = _contenido.GetById(Blogs);
        if (seccion is null || app is null || blogs is null)
        {
            var falta = seccion is null ? $"el tipo {Seccion}" : app is null ? $"el tipo {Bloque}" : $"la página {Blogs}";
            return new ResultadoDeLaComposicion(false, hechas, $"falta {falta}");
        }

        foreach (var pagina in Paginas)
        {
            var existente = _contenido.GetPagedChildren(blogs.Id, 0, 500, out _)
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
                .Set("cssClass", ClaseDeLaSeccion)
                .Set("spacingTop", "[\"none\"]")
                .Set("spacingBottom", "[\"none\"]")
                .Set("spacingInline", "[\"none\"]")
                .ApplyDefaults(_defectos.DefaultsFor(seccion.Key))
                .AddChild(AreaDeLaSeccion, app.Key, bloque => bloque
                    .Set("vista", $"[\"{pagina.Vista}\"]")
                    .ApplyDefaults(_defectos.DefaultsFor(app.Key)));

            var nueva = _contenido.Create(pagina.Nombre, blogs.Id, "pageBase");
            // El contenido VARÍA POR CULTURA: sin el nombre de la cultura, guardar falla con «empty name».
            nueva.SetCultureName(pagina.Nombre, Cultura);
            nueva.SetValue(Rejilla, rejilla.Build(), Cultura);
            if (nueva.HasProperty("hideFromMainMenu"))
            {
                nueva.SetValue("hideFromMainMenu", 1);
            }

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
