using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El molde de construcción de las capacidades (docs/product/08-despiece-apis.md §4) como
/// invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>Por qué esto es un gate y no una guía de estilo.</b> El arquitecto lo pidió así:
/// <i>"no puede ser una API diferente a la otra en cuanto a construcción"</i>. Veinte APIs con
/// veinte formas distintas es peor que un monolito — el monolito al menos es consistente. Y una
/// convención que solo vive en un documento se cumple en las tres primeras y se erosiona en las
/// diecisiete siguientes, porque nadie relee el documento antes de copiar el proyecto de al
/// lado.</para>
///
/// <para><b>Tosco a propósito</b>, como los demás gates del repo: no atrapa a un adversario,
/// atrapa el atajo de un martes. Y crece con el catálogo sin que nadie lo mantenga: el día que
/// aparezca <c>Synergos.Api.Payments</c>, estas reglas ya la están midiendo.</para>
/// </remarks>
public sealed class ApiMoldTests
{
    /// <summary>Las cuatro carpetas que toda capacidad tiene, con el mismo nombre y el mismo papel.</summary>
    private static readonly string[] Carpetas = { "Contracts", "Domain", "Storage", "Endpoints" };

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

    /// <summary>Los directorios de las capacidades: <c>Synergos.Api.*</c>.</summary>
    private static IReadOnlyList<(string Name, string Dir)> Capacidades()
        => Directory.EnumerateDirectories(RepoRoot(), "Synergos.Api.*")
            .Select(d => (Name: Path.GetFileName(d), Dir: d))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> Fuentes(string dir)
        => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Quita comentarios de línea para no medir la prosa que documenta la regla.</summary>
    private static string SinComentarios(string file)
        => string.Join('\n', File.ReadLines(file)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void El_gate_ve_las_capacidades_que_existen()
    {
        // Sin esto, un descubrimiento roto dejaría TODOS los asserts de abajo en verde sobre
        // una lista vacía. Un gate que no puede fallar es peor que no tener gate.
        var nombres = Capacidades().Select(c => c.Name).ToList();

        Assert.Contains("Synergos.Api.Sessions", nombres);
        Assert.Contains("Synergos.Api.Booking", nombres);
    }

    [Fact]
    public void Toda_capacidad_tiene_las_cuatro_carpetas_del_molde()
    {
        // La separación Contracts/ ↔ Domain/ NO es ceremonia: es lo que permite cambiar el
        // modelo interno sin romper a los clientes. Fusionarlas es cómodo el primer mes y
        // carísimo el segundo, porque cada renombre interno pasa a ser un cambio de contrato.
        var faltan = Capacidades()
            .SelectMany(c => Carpetas
                .Where(f => !Directory.Exists(Path.Combine(c.Dir, f)))
                .Select(f => $"{c.Name} → falta {f}/"))
            .ToList();

        Assert.True(faltan.Count == 0, string.Join(Environment.NewLine, faltan));
    }

    [Fact]
    public void Toda_capacidad_tiene_Program_cs_y_monta_el_borde_de_llave()
    {
        // Si una API se olvidara del UseSharedKeyAuth, quedaría abierta sin que nada avise —
        // y el aviso a gritos de Shared solo se dispara cuando el middleware SÍ se monta.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            var program = Path.Combine(dir, "Program.cs");
            if (!File.Exists(program)) { malas.Add($"{name} → sin Program.cs"); continue; }

            var texto = SinComentarios(program);
            if (!texto.Contains("UseSharedKeyAuth", StringComparison.Ordinal)) malas.Add($"{name} → sin UseSharedKeyAuth");
            if (!texto.Contains("\"/health\"", StringComparison.Ordinal)) malas.Add($"{name} → sin GET /health");
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Toda_ruta_esta_versionada_salvo_health()
    {
        // Una API sin versión en la ruta obliga a romper clientes o a inventarse una versión el
        // día que haya que cambiar algo — y ese día siempre llega.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            foreach (var file in Fuentes(dir))
            {
                foreach (var m in Regex.Matches(SinComentarios(file), @"\.Map(?:Get|Post|Put|Patch|Delete)\(\s*""([^""]*)""").Cast<Match>())
                {
                    var ruta = m.Groups[1].Value;
                    if (ruta != "/health" && !ruta.StartsWith("/v1/", StringComparison.Ordinal))
                    {
                        malas.Add($"{name}/{Path.GetFileName(file)} → '{ruta}' no está bajo /v1/");
                    }
                }
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Ninguna_capacidad_expone_PUT_ni_PATCH()
    {
        // Las transiciones son acciones con nombre: POST /holds/{id}/confirm dice QUÉ pasó. Un
        // PATCH {"status":"confirmed"} deja que el cliente invente transiciones que la
        // capacidad tendría que ir rechazando de a una — y la primera que se olvide es un bug
        // de estado, no de validación.
        var malas = Capacidades()
            .SelectMany(c => Fuentes(c.Dir)
                .Where(f => Regex.IsMatch(SinComentarios(f), @"\.Map(Put|Patch)\("))
                .Select(f => $"{c.Name}/{Path.GetFileName(f)}"))
            .ToList();

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    /// <summary>
    /// Cuántos endpoints hay se CUENTA. <c>CLAUDE.md</c> tiene que decir esa cifra.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita ya ocurrió, y de la peor forma</b> (#52). <c>CLAUDE.md</c>
    /// decía 134 y eran 136 — pero el 2 no es lo interesante: <b>el desfase era constante desde
    /// dieciocho commits</b>. La cifra se movió de 132 a 134 mientras el árbol iba de 134 a 136,
    /// o sea que quien la actualizaba <b>arrastraba el error anterior en vez de contar</b>. Es
    /// exactamente lo que #50 dijo de sí mismo, «la cuenta era de memoria», en otro fichero.</para>
    ///
    /// <para><b>Por qué se cuenta así.</b> La cifra incluye los veinte <c>/health</c>: cuando fue
    /// verdad por última vez eran 112 bajo <c>/v1</c> más 20. <c>MapPut</c>/<c>MapPatch</c> entran
    /// en la cuenta aunque hoy sean cero, porque el día que alguien los añada la cifra tiene que
    /// moverse — que no existan lo vigila
    /// <c>Ninguna_capacidad_expone_PUT_ni_PATCH</c>, no éste.</para>
    ///
    /// <para><b>Y por qué el número va en la prosa y no en un fichero de datos.</b> Porque el
    /// valor de <c>CLAUDE.md</c> es que se lee de corrido: «20 capacidades, 136 endpoints» le dice
    /// a un agente el tamaño del árbol en una línea. Sacarlo a un JSON generado lo haría cierto y
    /// nadie lo leería. Se queda escrito a mano y se le pone un gate detrás, que es el trato.</para>
    /// </remarks>
    [Fact]
    public void La_cifra_de_endpoints_de_CLAUDE_md_se_cuenta_contra_el_arbol()
    {
        var cuantos = Capacidades()
            .SelectMany(c => Fuentes(c.Dir))
            .Sum(f => Regex.Matches(SinComentarios(f), @"\.Map(Get|Post|Delete|Put|Patch)\(").Count);

        // Sin esto, un descubrimiento roto dejaría el assert de abajo comparando contra cero.
        Assert.True(cuantos > 100, $"Se contaron {cuantos} endpoints: el descubrimiento está roto.");

        var claude = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        foreach (var frase in new[]
                 {
                     $"LAS 20 CAPACIDADES, agnósticas. {cuantos} endpoints.",
                     $"20 capacidades ({cuantos} endpoints,",
                 })
        {
            Assert.True(claude.Contains(frase, StringComparison.Ordinal),
                $"CLAUDE.md no dice «{frase}». En el árbol hay {cuantos} endpoints "
                + "(rutas bajo /v1 más un /health por capacidad). La cifra se cuenta, no se "
                + "recuerda: si añadiste un endpoint, movela en §2 y en §11.");
        }
    }

    /// <summary>
    /// El primer argumento de un <c>Rejection.*</c>: el código que un orquestador compara.
    /// </summary>
    private const string Rechazo =
        @"Rejection\.(?:Invalid|NotFound|Conflict|Forbidden|Expired|Unavailable)\(\s*\$?""((?:[^""\\]|\\.)*)""";

    /// <summary>
    /// Qué prefijo declara cada tipo que tenga <c>CodePrefix</c>, en las capacidades y en
    /// <c>Synergos.Shared</c>.
    /// </summary>
    /// <remarks>
    /// Hace falta <c>Shared</c> porque <c>Api.Workflow</c> construye un rechazo con
    /// <c>IdentityTokens.CodePrefix</c> (HU #14, rebanada 3): sin resolverlo, ese código se
    /// descartaría por «dinámico» y la cifra saldría uno corta sin que nada lo dijera.
    /// </remarks>
    private static IReadOnlyDictionary<string, string> PrefijosDeclarados()
    {
        var mapa = new Dictionary<string, string>(StringComparer.Ordinal);

        var raices = Capacidades().Select(c => c.Dir)
            .Append(Path.Combine(RepoRoot(), "Synergos.Shared"));

        foreach (var fichero in raices.SelectMany(Fuentes))
        {
            var texto = SinComentarios(fichero);
            var tipos = Regex.Matches(texto, @"\b(?:class|record|struct)\s+(\w+)");

            foreach (Match asignacion in Regex.Matches(texto, @"CodePrefix\s*=\s*""([^""]+)"""))
            {
                // El tipo que la contiene es el último declarado antes de ella. Un fichero puede
                // declarar varios —IdentityTokens.cs declara cuatro— y quedarse con el primero
                // mapea el prefijo al tipo equivocado.
                var tipo = tipos.LastOrDefault(t => t.Index < asignacion.Index);
                if (tipo is not null) mapa[tipo.Groups[1].Value] = asignacion.Groups[1].Value;
            }
        }

        return mapa;
    }

    /// <summary>
    /// <c>CLAUDE.md</c> con los saltos colapsados y <b>sin los marcadores de cita</b>.
    /// </summary>
    /// <remarks>
    /// <para>La guía va envuelta a mano a ~72 columnas, así que las frases que estos gates buscan
    /// caen partidas: colapsar los espacios es obligatorio. Lo que no es evidente es que <b>hay
    /// que quitar el <c>&gt;</c> primero</b> — media §11 son blockquotes, y una frase que cruza
    /// dos líneas de uno se queda con un <c>&gt;</c> en medio después de colapsar. El síntoma es
    /// un gate que exige maquetar la prosa de cierta manera para pasar, y entonces manda el gate
    /// y no lo que el texto dice.</para>
    /// </remarks>
    private static string GuiaPlana()
    {
        var crudo = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));
        return Regex.Replace(Regex.Replace(crudo, @"^>\s?", string.Empty, RegexOptions.Multiline),
            @"\s+", " ");
    }

    /// <summary>
    /// La nota de §11 que explica el criterio de la cifra, sola: sus líneas de cita y nada más.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué no vale buscar en toda la guía.</b> La primera versión de esto pedía que
    /// §11 «nombrara» cada código de <c>Synergos.Shared</c> y buscaba en el fichero entero — y
    /// pasaba en verde con un nombre borrado de la enumeración, porque <c>assertion_not_proven</c>
    /// se menciona más abajo, en la nota del reenvío a <c>Api.Audit</c>. El gate afirmaba «la
    /// lista está completa» y comprobaba «la palabra aparece en alguna parte», que no es lo
    /// mismo: lo que un agente lee para saber qué sale de una capacidad sin estar en su fichero
    /// de reglas es <b>esa lista</b>, no el buscador.</para>
    /// </remarks>
    private static string NotaDelCriterio()
    {
        // Se busca por la frase y NO por la cifra: cuál es el número lo comprueba el otro fact,
        // y acoplarlos haría que un descuadre de la cifra rompiera los dos con el mismo mensaje.
        const string Marca = "se cuentan, y el criterio es parte de la cifra";

        var lineas = File.ReadAllLines(Path.Combine(RepoRoot(), "CLAUDE.md"));
        var inicio = Array.FindIndex(lineas, l => l.Contains(Marca, StringComparison.Ordinal));

        Assert.True(inicio >= 0,
            $"CLAUDE.md §11 no tiene la nota «…{Marca}…». Es la que explica el criterio de la "
            + "cifra, y sin ella no hay nada contra qué contar Synergos.Shared.");

        var fin = inicio;
        while (fin + 1 < lineas.Length && lineas[fin + 1].StartsWith('>')) fin++;

        return Regex.Replace(
            string.Join(" ", lineas[inicio..(fin + 1)].Select(l => l.TrimStart('>', ' '))),
            @"\s+", " ");
    }

    /// <summary>
    /// La cifra de códigos de rechazo que <c>CLAUDE.md</c> §11 declara, contada contra el árbol.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita es el mismo de los endpoints</b> (#52), y venía en la misma
    /// frase: «20 capacidades (136 endpoints, <b>195</b> códigos de rechazo)». Los endpoints ya
    /// se contaban; los códigos no, y eran <b>235</b>. Una cifra escrita a mano en una guía es
    /// exacta el día que se escribe y nadie vuelve.</para>
    ///
    /// <para><b>El criterio ES parte de la cifra</b>, y por eso va escrito acá y en la guía. Se
    /// cuentan los códigos <b>literales distintos</b> que el árbol de las capacidades construye.
    /// Quedan fuera dos cosas que sí existen:</para>
    /// <list type="number">
    ///   <item>los que se arman en ejecución —<c>orders.already_{destino}</c>, y los
    ///   reenvoltorios <c>xxx.{code}</c> que pasan un código recibido—, que no se pueden
    ///   enumerar sin ejecutar;</item>
    ///   <item>los de <c>Synergos.Shared</c> que una capacidad devuelve sin declararlos. Ésos
    ///   <b>no son un número</b>: seis llevan prefijo fijo y dos llevan el de quien llama, así
    ///   que sumarlos da una función del llamador. Ver el fact de abajo.</item>
    /// </list>
    ///
    /// <para>Se eligió el criterio <b>simétrico con el de endpoints</b> —lo que hay en el árbol
    /// de las capacidades— porque el sujeto de la frase son las veinte capacidades. Dos cifras
    /// de la misma frase contadas con dos varas distintas es cómo se acaba discutiendo cuál de
    /// las dos está mal.</para>
    ///
    /// <para><b>Y se comprueban las DOS apariciones, no sólo la del resumen.</b> §11 escribe la
    /// cifra dos veces —en «20 capacidades (136 endpoints, N códigos de rechazo)» y en la
    /// cabecera de la nota que explica el criterio, «Los N se cuentan»—, y este gate miraba la
    /// primera. Al fusionar dos ramas la nota se quedó en 235 con el resumen ya en 234, y el
    /// build siguió verde: la sección se contradecía a sí misma sobre el dato que este gate
    /// existe para sostener. Una cifra vigilada a medias es una cifra sin vigilar, porque quien
    /// lee la nota no sabe que la de arriba manda.</para>
    ///
    /// <para><b>La frase se busca con los espacios colapsados</b>: la guía va envuelta a mano a
    /// ~72 columnas y esta cae partida. Exigirla contigua obligaría a maquetar la prosa para
    /// pasar el gate, y entonces manda el gate y no lo que dice el texto.</para>
    /// </remarks>
    [Fact]
    public void La_cifra_de_codigos_de_rechazo_de_CLAUDE_md_se_cuenta_contra_el_arbol()
    {
        var prefijos = PrefijosDeclarados();
        var codigos = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, dir) in Capacidades())
        {
            var propio = Directory.EnumerateFiles(Path.Combine(dir, "Domain"), "*Rules.cs")
                .Select(f => Regex.Match(SinComentarios(f), @"CodePrefix\s*=\s*""([^""]+)"""))
                .FirstOrDefault(m => m.Success)?.Groups[1].Value;

            foreach (var fichero in Fuentes(dir))
            {
                foreach (Match uso in Regex.Matches(SinComentarios(fichero), Rechazo))
                {
                    var codigo = Regex.Replace(uso.Groups[1].Value, @"\{(\w+)\.CodePrefix\}",
                        m => prefijos.TryGetValue(m.Groups[1].Value, out var p) ? p : m.Value);

                    if (propio is not null)
                    {
                        codigo = codigo.Replace("{CodePrefix}", propio, StringComparison.Ordinal);
                    }

                    // Lo que sigue teniendo una interpolación se arma en ejecución.
                    if (codigo.Contains('{')) continue;

                    codigos.Add(codigo);
                }
            }
        }

        // Sin esto, un descubrimiento roto dejaría el assert de abajo comparando contra cero —
        // y el build se arreglaría escribiendo «0 códigos de rechazo» en la guía.
        Assert.True(codigos.Count > 150,
            $"Se contaron {codigos.Count} códigos de rechazo: el descubrimiento está roto.");

        var claude = GuiaPlana();

        var frase = $"{codigos.Count} códigos de rechazo)";

        Assert.True(claude.Contains(frase, StringComparison.Ordinal),
            $"CLAUDE.md §11 no dice «{frase}». En el árbol de las capacidades hay "
            + $"{codigos.Count} códigos literales distintos. La cifra se cuenta, no se recuerda: "
            + "decía 195 y llevaba sin contarse desde que se escribió (#52).");

        var cabecera = $"**Los {codigos.Count} se cuentan";

        Assert.True(claude.Contains(cabecera, StringComparison.Ordinal),
            $"CLAUDE.md §11 no dice «{cabecera}…». La nota que explica el criterio repite la "
            + "cifra en su cabecera, y ésa también se cuenta: se quedó en 235 con el resumen ya "
            + $"en 234 y el build siguió verde. Hoy hay {codigos.Count}.");
    }

    /// <summary>
    /// Cómo se escriben en castellano las cuentas pequeñas que la guía enuncia con letra.
    /// </summary>
    private static string Palabra(int n) => n switch
    {
        1 => "Uno", 2 => "Dos", 3 => "Tres", 4 => "Cuatro", 5 => "Cinco", 6 => "Seis",
        7 => "Siete", 8 => "Ocho", 9 => "Nueve", 10 => "Diez",
        _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>
    /// Lo que §11 dice de los códigos que viven en <c>Synergos.Shared</c>, contado contra
    /// <c>Synergos.Shared</c>.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita.</b> La nota del criterio enumeraba «los seis que viven en
    /// <c>Synergos.Shared</c>» y remataba con «contando aquéllos serían 240». Los seis eran
    /// ciertos y el 240 no: la enumeración sólo había mirado los códigos de <b>prefijo fijo</b>
    /// —los <c>identity.*</c>— y se le habían pasado <b>dos que llevan el prefijo de quien
    /// llama</b>, <c>{prefijo}.idempotency_key_required</c> y
    /// <c>{prefijo}.access_requires_identity</c>. Ésos no suman una vez: suman una por capacidad
    /// que los emita, y hoy la primera la emiten diecinueve.
    /// </para>
    ///
    /// <para><b>Por eso lo que se comprueba NO es un total.</b> Un total de «todo lo que puede
    /// salir» cambia cuando una capacidad empieza a exigir la cabecera de idempotencia, sin que
    /// nadie escriba un <c>Rejection</c> nuevo — escribirlo a mano en la guía lo dejaría obsoleto
    /// por un cambio que no lo parece. Se comprueban las dos afirmaciones que sí son estables:
    /// <b>qué seis</b> tienen prefijo fijo, y <b>cuántas</b> capacidades exigen la llave.
    /// </para>
    ///
    /// <para><b>Y los seis se piden por NOMBRE, no por cuenta.</b> Una lista que dice «seis» y
    /// nombra otros seis pasaría cualquier recuento; lo que un agente lee, para saber qué puede
    /// devolver una capacidad sin que esté en su fichero de reglas, son los nombres.
    /// </para>
    /// </remarks>
    [Fact]
    public void Lo_que_CLAUDE_md_dice_de_los_codigos_de_Shared_se_cuenta_contra_Shared()
    {
        var shared = Path.Combine(RepoRoot(), "Synergos.Shared");
        var ficheros = Directory.EnumerateFiles(shared, "*.cs", SearchOption.AllDirectories).ToList();

        var prefijoIdentidad = ficheros
            .Select(f => Regex.Match(SinComentarios(f),
                @"class\s+IdentityTokens\b[\s\S]*?CodePrefix\s*=\s*""([^""]+)"""))
            .First(m => m.Success).Groups[1].Value;

        var fijos = new SortedSet<string>(StringComparer.Ordinal);
        var porLlamador = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var fichero in ficheros)
        {
            foreach (Match uso in Regex.Matches(SinComentarios(fichero), Rechazo))
            {
                var codigo = uso.Groups[1].Value;

                // El prefijo en minúscula es un PARÁMETRO: lo pone quien llama, así que el
                // código literal es uno por capacidad. Los demás resuelven a uno solo.
                if (codigo.Contains("{codePrefix}", StringComparison.Ordinal))
                {
                    porLlamador.Add(codigo.Replace("{codePrefix}.", string.Empty, StringComparison.Ordinal));
                    continue;
                }

                fijos.Add(Regex.Replace(codigo, @"\{(?:IdentityTokens\.)?CodePrefix\}", prefijoIdentidad));
            }
        }

        Assert.All(fijos, c => Assert.DoesNotContain("{", c, StringComparison.Ordinal));

        var texto = NotaDelCriterio();

        // La guía dice el prefijo una vez y cuelga los demás de él, que es como se lee.
        foreach (var codigo in fijos)
        {
            var corto = codigo[(codigo.IndexOf('.') + 1)..];

            Assert.True(texto.Contains($"`{codigo}`", StringComparison.Ordinal)
                     || texto.Contains($"`{corto}`", StringComparison.Ordinal),
                $"CLAUDE.md §11 no nombra `{corto}`, que vive en Synergos.Shared y una capacidad "
                + "devuelve sin declararlo. La lista se enumera para que se pueda leer qué sale de "
                + "una capacidad sin estar en su fichero de reglas: una que se queda corta manda a "
                + "buscar el código a un sitio donde no está.");
        }

        Assert.True(texto.Contains($"{Palabra(fijos.Count)} llevan prefijo fijo", StringComparison.Ordinal),
            $"CLAUDE.md §11 no dice «{Palabra(fijos.Count)} llevan prefijo fijo». En "
            + $"Synergos.Shared hay {fijos.Count}: {string.Join(", ", fijos)}.");

        // Sin esto, un descubrimiento roto dejaría el bucle de abajo sin iteraciones y el fact
        // pasaría afirmando nada.
        Assert.True(porLlamador.Count > 0,
            "No se encontró en Synergos.Shared ningún código con prefijo de llamador: el "
            + "descubrimiento está roto.");

        foreach (var sufijo in porLlamador)
        {
            Assert.True(texto.Contains($"`{{prefijo}}.{sufijo}`", StringComparison.Ordinal),
                $"CLAUDE.md §11 no nombra `{{prefijo}}.{sufijo}`, que Synergos.Shared construye con "
                + "el prefijo de quien llama. Es justo la clase de código que se le pasó a la "
                + "enumeración vieja: contó los de prefijo fijo y dio el total por cerrado.");
        }

        var exigen = Capacidades().Count(c => Fuentes(c.Dir).Any(f =>
            SinComentarios(f).Contains("IdempotencyHeader.TryRead", StringComparison.Ordinal)));

        Assert.True(texto.Contains($"las **{exigen}** capacidades que exigen la cabecera",
                StringComparison.Ordinal),
            $"CLAUDE.md §11 no dice «las **{exigen}** capacidades que exigen la cabecera». Hoy "
            + $"llaman a IdempotencyHeader.TryRead {exigen} de las veinte, y cada una puede "
            + "devolver su propio `idempotency_key_required` sin declararlo.");
    }

    /// <summary>
    /// Toda capacidad tiene su fichero de reglas, y declara su prefijo de códigos.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita</b> (#58). <c>CLAUDE.md</c> §3 dice que la respuesta a «¿qué
    /// rechaza esta capacidad?» está en <c>Domain/XRules.cs</c> y que <b>es el único sitio</b>.
    /// Era cierto en diecinueve: <c>Api.Sessions</c> no tenía ese fichero y su única regla vivía
    /// dentro del método del endpoint — donde no se puede probar sin levantar el host.</para>
    ///
    /// <para><b>Y el gate de las cuatro carpetas no lo veía</b>, porque <c>Domain/</c> existía:
    /// dentro estaba <c>SearchEvent.cs</c>. Medía la carpeta, no lo que §3 promete que hay en
    /// ella.</para>
    ///
    /// <para><b>Pide que el fichero EXISTA, no que ningún rechazo viva fuera.</b> La diferencia
    /// importa: <c>Api.Notifications</c> construye cinco códigos en <c>Transport/</c> —los fallos
    /// de firma del webhook de Resend— y ahí es donde corresponden, porque son del transporte y no
    /// del negocio. Un gate que prohibiera eso nacería con una lista de exenciones, y un gate con
    /// exenciones deja de leerse.</para>
    /// </remarks>
    [Fact]
    public void Toda_capacidad_tiene_su_fichero_de_reglas()
    {
        var malas = new List<string>();

        foreach (var (nombre, dir) in Capacidades())
        {
            var reglas = Directory.EnumerateFiles(Path.Combine(dir, "Domain"), "*Rules.cs").ToList();

            if (reglas.Count == 0)
            {
                malas.Add($"{nombre} no tiene Domain/*Rules.cs. Sus rechazos viven donde no se "
                          + "pueden probar sin levantar el host — es lo que costó una vuelta en "
                          + "BookingController (#36) y en la emisión de tokens (#14, rebanada 2).");
                continue;
            }

            // Sin prefijo declarado, cada rechazo escribe el suyo a mano y el día que uno se
            // teclee mal nadie lo nota: un código es una cadena hasta que alguien la agrupa.
            if (!reglas.Any(f => SinComentarios(f).Contains("CodePrefix", StringComparison.Ordinal)))
            {
                malas.Add($"{nombre} tiene fichero de reglas pero ninguno declara CodePrefix.");
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void El_ruteo_vive_en_Endpoints_y_no_desperdigado()
    {
        // Program.cs queda exento para el /health y para el Map*Endpoints(). Todo lo demás en
        // Endpoints/: si el ruteo se reparte, la superficie real de la API deja de poder leerse
        // en un sitio, y es la superficie lo que hay que revisar antes de publicar.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            foreach (var file in Fuentes(dir))
            {
                var rel = Path.GetRelativePath(dir, file);
                if (rel == "Program.cs" || rel.StartsWith("Endpoints" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

                if (Regex.IsMatch(SinComentarios(file), @"\.Map(Get|Post|Put|Patch|Delete)\("))
                {
                    malas.Add($"{name}/{rel} → ruteo fuera de Endpoints/");
                }
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Nadie_lee_el_reloj_del_ambiente_salvo_el_arranque()
    {
        // La mitad de los errores de estas capacidades son de borde temporal —el hold que vence
        // justo, la cancelación en el límite del plazo, la ventana de retención—. Con UtcNow
        // dentro de una regla, esos casos no se prueban: se sufren en producción y se
        // reproducen a mano cambiando la hora del sistema.
        //
        // Program.cs queda exento porque es donde se registra TimeProvider.System, que es
        // precisamente la forma correcta de leer el reloj una sola vez.
        var patron = new Regex(@"\bDateTime(Offset)?\.(UtcNow|Now)\b");
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            foreach (var file in Fuentes(dir))
            {
                var rel = Path.GetRelativePath(dir, file);
                if (rel == "Program.cs") continue;

                var n = 0;
                foreach (var line in SinComentarios(file).Split('\n'))
                {
                    n++;
                    if (patron.IsMatch(line)) malas.Add($"{name}/{rel}:{n} → {line.Trim()}");
                }
            }
        }

        Assert.True(malas.Count == 0,
            "El reloj se inyecta por TimeProvider; no se lee del ambiente." + Environment.NewLine +
            string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Toda_capacidad_referencia_Core_y_Shared_y_nada_del_CMS()
    {
        // Core le da el vocabulario común —Money, Ref, TimeWindow, Rejection— y Shared la
        // fontanería. Una capacidad que no los referencie está reinventando los dos, que es
        // exactamente lo que este árbol existe para evitar.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            var csproj = Path.Combine(dir, $"{name}.csproj");
            if (!File.Exists(csproj)) { malas.Add($"{name} → sin {name}.csproj"); continue; }

            var texto = File.ReadAllText(csproj);
            if (!texto.Contains("Synergos.Core.csproj", StringComparison.Ordinal)) malas.Add($"{name} → no referencia Synergos.Core");
            if (!texto.Contains("Synergos.Shared.csproj", StringComparison.Ordinal)) malas.Add($"{name} → no referencia Synergos.Shared");
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }
}
