using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El precio que tecleó un editor se lee en UN sitio.
/// </summary>
/// <remarks>
/// <para><b>Qué cierra</b> (#123). La regla de «solo dígitos» estaba escrita tres veces —Tienda,
/// Eventos, Educación— y <b>rota otras cinco</b>: el carrito, el listado, la ficha de producto, el
/// bloque de línea de carrito y el <c>ld+json</c> seguían con
/// <c>decimal.TryParse(raw, NumberStyles.Number, InvariantCulture) ? x : 0m</c>, o sea que
/// <c>"49.000"</c> se cobraba a <b>49</b> y <c>"$89000"</c> a <b>cero</b>.</para>
///
/// <para><b>Y lo que de verdad lo dejó vivo no fue la duplicación: fue el COMENTARIO.</b>
/// <c>UmbracoProductCatalogSource.TryParsePrice</c> arregló su lado y dejó escrito, con todas las
/// letras, que «el código viejo hace <c>decimal.TryParse(…) ? price : 0m</c>» y que «verificado en
/// vivo: el Tote salió a 49» — nombrando un defecto que vivía en otra pieza. Un defecto
/// identificado la siguiente auditoría lo lee y pasa de largo, que es exactamente lo que le pasó
/// al <c>unreadMessages</c> del home (#111 → #116). De ahí que este gate exista y no una tercera
/// nota.</para>
///
/// <para><b>El algoritmo NO es el criterio, y por eso esto no cuenta <c>decimal.TryParse</c>.</b>
/// <c>CatalogFilter</c> parsea <c>"1000-5000"</c> y <c>"4.5"</c> con <c>NumberStyles.Float</c> e
/// <c>InvariantCulture</c>, y ahí el punto decimal es CORRECTO: ese texto no lo teclea un editor,
/// lo genera el cliente en la query string, y con basura lo que se hace es no aplicar el filtro.
/// Mismo <c>TryParse</c>, otro sujeto. El criterio es <b>qué se está leyendo</b>: un precio
/// autorado.</para>
///
/// <para><b>Lo que este gate NO ve, dicho para no mentir sobre su alcance</b>
/// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>): lee la FUENTE con regex
/// sobre <c>Synergos.CMS.Web</c>. Una copia en otro proyecto, o una que llame a su variable
/// <c>monto</c> o <c>valor</c> y no mencione «price»/«precio» en las dos líneas de alrededor, se
/// le escapa. Por eso el primer diente va por el <b>campo del schema</b> —que sí es literal y no
/// se puede escribir de otra manera— y el segundo por el nombre.</para>
/// </remarks>
public sealed class PrecioUnicoTests
{
    private const string Helper = "PrecioAutorado.cs";

    /// <summary>Los tres campos de precio que un editor teclea como texto libre.</summary>
    /// <remarks>
    /// Son <c>Umbraco.TextBox</c> los tres (cambiarles el tipo exige Key nueva + migrar nodos),
    /// así que la lista es cerrada y literal: un cuarto campo de precio se añade acá y al helper
    /// a la vez. <c>unitPrice</c> es el del bloque editorial <c>elementShopCartItem</c>.
    /// </remarks>
    private static readonly string[] CamposDePrecio =
    {
        "productPriceBase", "eventPriceFrom", "coursePriceFrom", "unitPrice",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Synergos.CMS.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    /// <summary>El fichero sin comentarios: la prosa explica el código, no lo es.</summary>
    /// <remarks>
    /// <b>Sin esto el gate se dispararía con su propia explicación</b>: las tres fuentes de
    /// catálogo citan literalmente el <c>decimal.TryParse(raw, …, InvariantCulture)</c> viejo en
    /// sus <c>&lt;remarks&gt;</c>, que es justo lo que hay que dejar escrito. Cubre <c>//</c>,
    /// <c>///</c>, la continuación <c>*</c> de un bloque y el <c>@*…*@</c> de una vista de una
    /// sola línea.
    /// </remarks>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            return t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith("*", StringComparison.Ordinal)
                || t.StartsWith("@*", StringComparison.Ordinal)
                ? string.Empty
                : l;
        }));

    /// <summary>Todo el árbol del Web: código y vistas. El defecto vivía en los dos.</summary>
    private static List<(string Nombre, string Codigo)> ArbolDelWeb()
        => Directory
            .EnumerateFiles(Path.Combine(RepoRoot(), "Synergos.CMS.Web"), "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".cshtml", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(f => !Path.GetFileName(f).Equals(Helper, StringComparison.Ordinal))
            .Select(f => (Path.GetRelativePath(RepoRoot(), f), SinComentarios(f)))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void El_descubrimiento_ve_el_arbol_y_el_helper()
    {
        // Sin esto, los dientes de abajo recorrerían una lista vacía y el build quedaría verde
        // sobre nada — el trinquete al revés.
        var arbol = ArbolDelWeb();
        Assert.True(arbol.Count > 200, $"Sólo se vieron {arbol.Count} ficheros en Synergos.CMS.Web.");
        Assert.Contains(arbol, f => f.Nombre.EndsWith(".cshtml", StringComparison.Ordinal));

        var helper = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Services", Helper);
        Assert.True(File.Exists(helper), "No existe PrecioAutorado.cs: revisar este gate.");
        Assert.Contains("IsAsciiDigit", SinComentarios(helper), StringComparison.Ordinal);
        Assert.Contains("NumberStyles.None", SinComentarios(helper), StringComparison.Ordinal);

        // Y que los ocho consumidores siguen pasando por él (el carrito, vía ShopLinePricing).
        var porElHelper = arbol.Count(f => f.Codigo.Contains("PrecioAutorado.EsInequivoco(", StringComparison.Ordinal));
        Assert.True(porElHelper >= 8,
            $"Sólo {porElHelper} ficheros llaman al helper; eran OCHO al escribir esto (#123): las tres "
            + "fuentes de catálogo (Tienda, Eventos, Educación), ShopLinePricing, DefaultShopQuery y las "
            + "tres vistas (_ProductDetailCore, CartItem, _SeoStructuredData).");
    }

    /// <summary>
    /// EL DIENTE QUE IMPORTA: nadie lee uno de los campos de precio del schema y lo parsea por su
    /// cuenta.
    /// </summary>
    /// <remarks>
    /// Va por el <b>SUJETO</b> —el alias del campo, que es literal y no admite sinónimos— y no
    /// por el algoritmo. Cubre los cinco sitios que tenían el defecto y no toca
    /// <c>CatalogFilter</c>, que parsea otra cosa.
    /// </remarks>
    [Fact]
    public void Nadie_fuera_del_helper_parsea_un_campo_de_precio_del_schema()
    {
        var culpables = new List<string>();

        foreach (var (nombre, codigo) in ArbolDelWeb())
        {
            foreach (var campo in CamposDePrecio)
            {
                // El texto entre la lectura del campo y las ~6 líneas siguientes: es donde vive
                // el parseo en los cinco sitios que tenían el defecto.
                foreach (Match m in Regex.Matches(codigo, Regex.Escape(campo) + @"[\s\S]{0,400}"))
                {
                    if (Regex.IsMatch(m.Value, @"\b(?:decimal|double|float)\s*\.\s*(?:Try)?Parse\s*\("))
                    {
                        culpables.Add($"{nombre} → {campo}");
                    }
                }
            }
        }

        Assert.True(culpables.Count == 0,
            "Estos sitios parsean un precio autorado por su cuenta en vez de llamar a "
            + "`PrecioAutorado.EsInequivoco(...)`. Es lo que dejó ocho copias con la regla buena en "
            + "tres y el `decimal.TryParse(…, NumberStyles.Number, InvariantCulture) ? x : 0m` en "
            + "cinco: \"49.000\" se cobraba a 49 y \"$89000\" a cero (#123):"
            + Environment.NewLine + string.Join(Environment.NewLine, culpables.Distinct()));
    }

    /// <summary>
    /// El segundo diente, por lo que el primero no ve: un parseo cuya variable se llama precio
    /// pero que no menciona el campo del schema cerca.
    /// </summary>
    /// <remarks>
    /// <para>Hace falta porque el primero ancla en el alias: un helper que reciba el texto ya
    /// leído —<c>ParsePrice(string raw)</c>, que es justo la forma que tenía
    /// <c>DefaultShopQuery</c>— puede quedar a más de 400 caracteres del <c>Value&lt;string&gt;</c>
    /// que lo alimenta.</para>
    ///
    /// <para><b>No se puede exigir cero</b>, así que lleva trinquete: hoy es <b>uno</b>
    /// —<c>CatalogFilter</c> no está acá, pero <c>StubClinicalResultsProvider</c> tampoco es Web,
    /// y dentro de Web queda <c>RealtyController</c>, que parsea una COORDENADA de la query
    /// string y sólo cae acá porque su variable vecina se llama <c>price</c> en el mismo método
    /// de filtros—. Un segundo rompe el build y obliga a decir por qué.</para>
    /// </remarks>
    [Fact]
    public void Los_parseos_numericos_cerca_de_un_precio_llevan_trinquete()
    {
        var conLaForma = new List<string>();

        foreach (var (nombre, codigo) in ArbolDelWeb())
        {
            foreach (Match m in Regex.Matches(
                         codigo, @"\b(?:decimal|double|float)\s*\.\s*(?:Try)?Parse\s*\([^;]{0,300}"))
            {
                if (Regex.IsMatch(m.Value, @"(?i)\b\w*(price|precio|monto|amount)\w*\b"))
                {
                    conLaForma.Add($"{nombre} → {m.Value.Split('\n')[0].Trim()}");
                }
            }
        }

        Assert.True(conLaForma.Count == ParseosDePrecioFueraDelHelper,
            $"Parseos numéricos con pinta de precio fuera del helper hay {conLaForma.Count} y el "
            + $"trinquete está en {ParseosDePrecioFueraDelHelper}:"
            + Environment.NewLine + string.Join(Environment.NewLine, conLaForma)
            + Environment.NewLine
            + "Si es un precio autorado, llamá a `PrecioAutorado.EsInequivoco(...)`. Si NO lo es "
            + "—como un rango de la query string o una coordenada— subí el trinquete y escribí acá "
            + "qué lee y por qué el punto decimal es correcto ahí. Mismo TryParse no es el mismo "
            + "sujeto (#120).");
    }

    /// <summary>
    /// Lo que queda legítimamente con la forma de un parseo de precio fuera del helper.
    /// </summary>
    /// <remarks>
    /// <b>Trinquete y no lista de excepciones</b>, la forma de <c>FormasAjenasAlSeudonimo</c> y de
    /// <c>contract-keys.baseline.json</c>: hoy es <b>cero</b>. El día que haya uno, se sube y se
    /// escribe qué lee — que es exactamente la conversación que no se tuvo cinco veces.
    /// </remarks>
    private const int ParseosDePrecioFueraDelHelper = 0;

    /// <summary>
    /// El carrito no puede volver a cobrar en cero: su política es OMITIR la línea.
    /// </summary>
    /// <remarks>
    /// El diente por el que un gate de «quién parsea» no pasa: alguien puede llamar al helper y
    /// escribir <c>EsInequivoco(raw, out var p) ? p : 0m</c>, que es el mismo defecto delegado.
    /// La forma que lo impide es que <c>ShopLinePricing.TryUnitPrice</c> devuelva <c>bool</c> —
    /// «no se pudo» como propiedad del TIPO y no como convención— y que el carrito haga
    /// <c>continue</c> dejando constancia.
    /// </remarks>
    [Fact]
    public void El_carrito_omite_la_linea_sin_precio_y_lo_deja_escrito()
    {
        var carrito = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "DefaultCartService.cs"));

        Assert.Contains("ShopLinePricing.TryUnitPrice(", carrito, StringComparison.Ordinal);
        Assert.Contains("_logger.LogError(", carrito, StringComparison.Ordinal);

        // Y que la firma sigue sin dejar decir «no se sabe» con un número.
        var pricing = SinComentarios(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Services", "ShopLinePricing.cs"));
        Assert.Contains("public static bool TryUnitPrice(", pricing, StringComparison.Ordinal);
    }
}
