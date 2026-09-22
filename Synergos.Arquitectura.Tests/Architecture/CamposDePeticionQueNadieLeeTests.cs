using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Todo campo que un cuerpo de petición DECLARA lo LEE alguien (#160).
/// </summary>
/// <remarks>
/// <para><b>El defecto que cierra.</b> <c>RealtyController.VisitRequest</c> declaraba
/// <c>Mode</c> —la modalidad de una visita: presencial o videollamada—, el binder la enlazaba
/// sin quejarse, y <b>el método no la leía ni una vez</b>. Quien pedía videollamada quedaba
/// agendado sin que nada lo dijera, ni en la constancia ni en la agenda del agente, que sí tiene
/// el campo y lo sacaba del mock.</para>
///
/// <para><b>Y lo invisible no era el hueco: era que los dos gates de contrato estaban EN LO
/// SUYO.</b> G-7 (<c>tools/contract-bodies.mjs</c>) cruza «lo que la app MANDA ↔ lo que el borde
/// DECLARA», y <c>mode</c> <i>estaba declarado</i>: cruzaba. G-6
/// (<c>tools/contract-keys.mjs</c>) cruza «lo que el borde EMITE ↔ lo que la app LEE», y el
/// borde no lo emitía — ni el consumidor lo leía de la respuesta, porque se lo copiaba de su
/// propia petición. O sea: <b>declarar es lo que se mide y leer es lo que importa</b>, y entre
/// las dos preguntas cabe entero un campo que viaja por el cable y se tira al suelo.</para>
///
/// <para><b>El tell, y se busca sin leer lógica:</b> una propiedad de un contrato de entrada que
/// no aparece con su receptor delante en ninguna parte. Es el espejo de
/// <c>feedback_no_read_without_a_write_path</c> —allá una lectura sin camino de escritura, acá
/// una ENTRADA sin camino de lectura— y el primo de
/// <c>feedback_every_authored_field_needs_a_reader</c>, que hace el mismo cruce un escalón más
/// abajo: allá el campo lo escribe un editor en un DocType, acá lo manda una app por HTTP, y en
/// los dos casos <b>el eslabón es un nombre escrito dos veces que ningún compilador comprueba</b>.</para>
///
/// <para><b>Su primera versión pasó en VERDE con el defecto puesto</b>, y sólo lo destapó
/// mutarlo. Buscaba <c>.Mode</c> en el fichero entero, y <c>MyVisits</c> escribe
/// <c>Mode: v.Mode</c> sobre un <c>PersistedVisit</c> — otro objeto, el mismo nombre. O sea que
/// medía «alguien nombra esta propiedad» y no «alguien LEE ESTE campo», que es literalmente la
/// distinción que el gate existe para hacer
/// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>). Hoy se exige el
/// RECEPTOR: la variable a la que el fichero ata ese record.</para>
///
/// <para><b>Por qué es trinquete absoluto y no línea base</b>: medido al escribirlo, de las
/// <b>146</b> propiedades de <c>*Request</c> de los controllers sólo <b>nueve</b> no se leen, y
/// las nueve son el mismo caso deliberado y documentado. Con el árbol ya cumpliendo, exigirlo
/// cuesta un censo en vez del muro de excepciones que deja de leerse (#134; el criterio
/// contrario es el del #140, donde la deuda era del 29 % y tocaba línea base).</para>
/// </remarks>
public sealed class CamposDePeticionQueNadieLeeTests
{
    /// <summary>
    /// Las excepciones, con su razón. <b>Las nueve son el MISMO caso</b>: un campo de identidad
    /// que el borde dejó de creerle al llamador —quién actúa sale del gate de sesión desde el
    /// barrido T2— y que se conserva declarado, y nulable, para que un cliente viejo que todavía
    /// lo mande no se coma un 400 de la validación automática de <c>[ApiController]</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>Que sean todas la misma es lo que las hace legibles.</b> Un censo de nueve
    /// razones distintas sería el muro que deja de leerse; nueve filas de una sola regla se leen
    /// de un vistazo, y la décima que no encaje salta.</para>
    ///
    /// <para><b>Se vigila en los DOS sentidos</b>: una fila que ya no corresponda rompe el build
    /// igual que un campo huérfano sin declarar. Un censo vigilado en un solo sentido se queda
    /// afirmando que el defecto sigue ahí después de que alguien lo arregló
    /// (<c>feedback_a_census_entry_is_how_a_defect_survives_its_own_gate</c>) — y lo que
    /// distingue a estas nueve de un ticket sin abrir es que su razón contesta «por qué esto NO
    /// se lee» y no «por qué todavía no se leyó».</para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> Censo = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["RealtyController.cs:SaveSearchRequest.User"] =
            "la identidad sale de RequireUser(); nulable e ignorado por compatibilidad con clientes previos al barrido T2",
        ["RealtyController.cs:FavoriteRequest.User"] =
            "mismo caso que SaveSearchRequest.User",
        ["BlogsController.cs:SaveRequest.User"] =
            "el dueño lo pone RequireActor(), no el cuerpo",
        ["BlogsController.cs:CreatePostRequest.AuthorId"] =
            "el autor lo pone RequireActor(); aceptarlo del cuerpo era firmar un post a nombre de cualquiera",
        ["BlogsController.cs:CreateArticleRequest.Author"] =
            "mismo caso que CreatePostRequest.AuthorId",
        ["BlogsController.cs:SendMessageRequest.From"] =
            "el remitente lo pone RequireActor(); del cuerpo era escribir a nombre de otro",
        ["ShopCatalogController.cs:WishlistItemRequest.Owner"] =
            "el dueño lo pone RequireMemberEmail()",
        ["ShopCatalogController.cs:StartThreadRequest.From"] =
            "mismo caso que WishlistItemRequest.Owner",
        ["ShopCatalogController.cs:ReplyRequest.From"] =
            "mismo caso que WishlistItemRequest.Owner",
    };

    /// <summary>
    /// Piso de descubrimiento. Si el recorte de los records dejara de casar, las dos listas
    /// saldrían vacías y el cruce pasaría en verde sin mirar nada — el defecto que el #136 midió
    /// (12/12 sobre una lista vacía). Eran 146 al escribirlo.
    /// </summary>
    private const int MinimoDePropiedades = 120;

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

    /// <summary>
    /// La fuente sin comentarios ni literales de cadena.
    /// </summary>
    /// <remarks>
    /// <para><b>Los COMENTARIOS son la mitad que sostiene el gate, y está medido.</b> Quitar una
    /// lectura casi nunca deja el hueco limpio: deja una nota que la nombra —«la modalidad llega
    /// en <c>request.Mode</c> y se normaliza más adelante»— que es exactamente la forma en que
    /// este repo ya vio blindarse un defecto. Mutado: con el defecto puesto <b>y</b> ese
    /// comentario al lado, el gate sigue ROJO. Sin este recorte se pondría verde leyendo su
    /// propia explicación, que es el tropiezo de
    /// <c>feedback_a_seam_that_a_view_bypasses_is_not_a_seam</c>.</para>
    ///
    /// <para><b>Los LITERALES no cambian el resultado hoy, y decirlo importa más que quitarlos.</b>
    /// Mutado también: sin recortar cadenas el gate da lo mismo, porque exigir el receptor ya hace
    /// que un <c>"mode"</c> suelto no se parezca a <c>request.Mode</c>. O sea que acá el recorte
    /// NO es la medida —al revés que en #148, donde sin él el numerador caía de 49 a 14—. Se
    /// conserva porque cuesta tres líneas y cierra el caso en que un literal contenga algo con la
    /// forma de una declaración y fabrique un receptor fantasma; afirmarlo como imprescindible
    /// sería documentación que la propia mutación desmiente.</para>
    /// </remarks>
    private static string Desnuda(string fuente)
    {
        var sinBloques = Regex.Replace(fuente, @"/\*[\s\S]*?\*/", string.Empty);
        var sinLinea = string.Join('\n', sinBloques
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        // Las tres formas de literal del repo, de la más larga a la más corta: recortar `"…"`
        // antes que `"""…"""` partiría una cadena cruda por la mitad y dejaría código suelto.
        var sinCrudas = Regex.Replace(sinLinea, "\"\"\"[\\s\\S]*?\"\"\"", "\"\"");
        var sinVerbatim = Regex.Replace(sinCrudas, "@\"(?:[^\"]|\"\")*\"", "\"\"");
        return Regex.Replace(sinVerbatim, "\"(?:[^\"\\\\\\n]|\\\\.)*\"", "\"\"");
    }

    /// <summary>Los nombres posicionales de un <c>record X(...)</c>, en orden.</summary>
    private static IReadOnlyList<string> Propiedades(string parametros)
        => Regex.Matches(
                parametros,
                @"(?:^|,)\s*(?:\[[^\]]*\]\s*)?[\w<>\?\.\[\]]+\s+(\w+)\s*(?:=[^,]*)?(?=,|$)")
            .Select(m => m.Groups[1].Value)
            .ToList();

    [Fact]
    public void Todo_campo_que_un_cuerpo_de_peticion_declara_lo_lee_alguien()
    {
        var controllers = Path.Combine(RepoRoot(), "Synergos.CMS.Web", "Controllers");
        Assert.True(Directory.Exists(controllers), $"no está {controllers}");

        var total = 0;
        var huerfanos = new List<string>();

        foreach (var fichero in Directory.EnumerateFiles(controllers, "*.cs").OrderBy(f => f, StringComparer.Ordinal))
        {
            var nombre = Path.GetFileName(fichero);
            var src = Desnuda(File.ReadAllText(fichero));

            foreach (Match decl in Regex.Matches(src, @"record\s+(\w*Request)\s*\(([^;]*?)\)\s*;", RegexOptions.Singleline))
            {
                var record = decl.Groups[1].Value;

                // El resto del fichero SIN la declaración: dentro de ella el nombre aparece
                // siempre, así que buscarlo ahí daría «lo lee» para todos.
                var resto = src[..decl.Index] + src[(decl.Index + decl.Length)..];

                // Los RECEPTORES: las variables que el fichero ata a este record — el parámetro
                // `[FromBody] XRequest? request` y cualquier local declarada con su tipo.
                var receptores = Regex.Matches(resto, @"\b" + Regex.Escape(record) + @"\??\s+(\w+)\b")
                    .Select(m => m.Groups[1].Value)
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                foreach (var prop in Propiedades(decl.Groups[2].Value))
                {
                    total++;
                    if (!SeLee(resto, receptores, prop))
                    {
                        huerfanos.Add($"{nombre}:{record}.{prop}");
                    }
                }
            }
        }

        Assert.True(
            total >= MinimoDePropiedades,
            $"sólo se vieron {total} propiedades de *Request y hay más de {MinimoDePropiedades}. " +
            "El recorte de los records está roto: sin esto el cruce de abajo pasaría en verde sin mirar nada.");

        var sinDeclarar = huerfanos.Where(h => !Censo.ContainsKey(h)).OrderBy(h => h, StringComparer.Ordinal).ToList();
        Assert.True(
            sinDeclarar.Count == 0,
            "Estos campos los enlaza el binder y NO los lee nadie:\n  " +
            string.Join("\n  ", sinDeclarar) +
            "\n\nUn campo así no falla: viaja por el cable y se tira al suelo, y los dos gates de " +
            "contrato lo dan por bueno — G-7 porque está DECLARADO y G-6 porque el borde no lo " +
            "emite. O se lee, o entra al censo de este gate con la razón por la que NO se lee " +
            "(no «todavía no»). Fue el defecto #160: la modalidad de una visita.");

        var sobran = Censo.Keys.Except(huerfanos, StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.True(
            sobran.Count == 0,
            "Estas filas del censo ya no corresponden — el campo se lee (o el record se fue):\n  " +
            string.Join("\n  ", sobran) +
            "\n\nUna excepción que sobra deja de leerse, y un censo vigilado en un solo sentido se " +
            "queda afirmando un defecto que alguien ya arregló.");
    }

    /// <summary>
    /// ¿Alguien lee <paramref name="prop"/> de este record?
    /// </summary>
    /// <remarks>
    /// <para><b>Con receptor conocido se exige <c>receptor.Prop</c></b>, que es lo único que
    /// distingue leer ESTE campo de nombrar una propiedad que se llama igual en otro tipo — el
    /// verde falso que costó la primera versión de este gate.</para>
    ///
    /// <para><b>Sin receptor se cae al criterio débil, y va dicho para no mentir sobre el
    /// alcance.</b> Los records ANIDADOS —<c>CheckoutItemRequest</c>, <c>CartItemRequest</c>,
    /// <c>CourseDraftLessonRequest</c>…— no se atan nunca a una variable de su tipo: se leen
    /// dentro de un <c>foreach (var item in request.Items)</c>, así que su receptor es una local
    /// sin tipo escrito y seguirla exigiría resolver el elemento de la colección. Son <b>14 de
    /// las 146</b>, y para ésas esto comprueba que alguien nombre la propiedad y no que la lea de
    /// aquí. Cerrarlo del todo es otro trabajo (un parser de verdad), y decirlo es mejor que un
    /// gate que se cree más listo de lo que es.</para>
    /// </remarks>
    private static bool SeLee(string fuente, IReadOnlyList<string> receptores, string prop)
    {
        if (receptores.Count == 0)
        {
            return Regex.IsMatch(fuente, @"\." + Regex.Escape(prop) + @"\b");
        }

        return receptores.Any(r =>
            Regex.IsMatch(fuente, @"\b" + Regex.Escape(r) + @"[\?\!]?\." + Regex.Escape(prop) + @"\b"));
    }
}
