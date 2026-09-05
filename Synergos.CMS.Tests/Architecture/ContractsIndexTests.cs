using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El índice de los contratos CMS↔UI está completo, y la guía no declara bloqueado lo que no
/// lo está (#74).
/// </summary>
/// <remarks>
/// <para><b>Los dos defectos que evita ya estaban puestos</b>, y son la lección de #53 en las
/// dos direcciones a la vez:</para>
///
/// <list type="number">
///   <item>el índice mandaba a esperar por algo que ya llegó — su tabla decía «CDN team (CMS
///   bloqueado esperando)» sobre un bloqueo que terminó con la HU #20;</item>
///   <item>y escondía el contrato que sí hay que leer: <c>cdn-bundle-structure.md</c> vive en la
///   carpeta, marcado <c>Canónico</c>, y no aparecía en la tabla.</item>
/// </list>
///
/// <para><b>No es una carpeta cualquiera.</b> <c>CLAUDE.md</c> §3 manda acá diciendo que es «la
/// ÚNICA superficie de acople» con el UI, así que un índice equivocado desvía a quien viene a
/// integrarse — que es justo quien menos contexto tiene para notarlo.</para>
/// </remarks>
public sealed class ContractsIndexTests
{
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

    private static string CarpetaContratos()
        => Path.Combine(RepoRoot(), "Synergos.CMS.Web", "docs", "contracts");

    private static string Indice() => File.ReadAllText(Path.Combine(CarpetaContratos(), "README.md"));

    /// <summary>Los contratos del disco: todo <c>.md</c> de la carpeta menos su propio índice.</summary>
    private static IReadOnlyList<string> EnDisco()
        => Directory.EnumerateFiles(CarpetaContratos(), "*.md")
            .Select(Path.GetFileName)
            .Select(n => n!)
            .Where(n => !string.Equals(n, "README.md", StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Los ficheros que la tabla enlaza. Se lee el ENLACE y no el texto.
    /// </summary>
    /// <remarks>
    /// Por la misma razón que <c>AdrIndexTests</c>: el nombre suelto aparece en la prosa —«ver
    /// <c>host-bridge.md</c>»— y contarlo daría por indexado algo sólo mencionado de pasada.
    /// </remarks>
    private static IReadOnlyList<string> Enlazados()
        => Regex.Matches(Indice(), @"\]\((?!\.\./|https?:)([^)/#]+\.md)\)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Todo_contrato_del_disco_esta_en_el_indice()
    {
        var disco = EnDisco();

        // Sin esto, un descubrimiento roto dejaría el assert comparando dos listas vacías.
        Assert.True(disco.Count >= 5, $"Se encontraron {disco.Count} contratos: el descubrimiento está roto.");

        var enlazados = Enlazados();
        var faltan = disco.Where(d => !enlazados.Contains(d, StringComparer.Ordinal)).ToList();

        Assert.True(faltan.Count == 0,
            $"Estos contratos están en docs/contracts/ y el README no los enlaza: {string.Join(", ", faltan)}. "
            + "Es el caso de `cdn-bundle-structure.md` (#74): canónico, en la carpeta, y fuera de su "
            + "propio índice — o sea invisible para quien viene a integrarse.");
    }

    /// <summary>
    /// El índice no enlaza contratos que no existen.
    /// </summary>
    /// <remarks>
    /// La segunda mitad no es simetría por gusto: una entrada que apunta a un fichero que no está
    /// es <b>peor</b> que la ausencia, porque parece que hay algo y no lo hay. Es lo que la tabla
    /// hacía con <c>cdn-bundle-registry.md</c>, un nombre que nunca existió en esa carpeta.
    /// </remarks>
    [Fact]
    public void El_indice_no_enlaza_contratos_que_no_existen()
    {
        var disco = EnDisco();
        var fantasmas = Enlazados().Where(e => !disco.Contains(e, StringComparer.Ordinal)).ToList();

        Assert.True(fantasmas.Count == 0,
            $"El README de contratos enlaza ficheros que no están en la carpeta: {string.Join(", ", fantasmas)}.");
    }

    /// <summary>
    /// Si §9 dice que no hay bloqueos vigentes, la guía no declara nada bloqueado.
    /// </summary>
    /// <remarks>
    /// <para><b>La guía se contradecía consigo misma</b> (#74): §2 —el mapa del proyecto, lo
    /// primero que abre alguien que no conoce el repo— rotulaba <c>cdn-contract.md</c> como
    /// «externalmente bloqueado», y §9 decía «Bloqueos vigentes: ninguno» cien líneas más abajo.
    /// El propio fichero rotulado abre con <c>✅ DESBLOQUEADO</c>.</para>
    ///
    /// <para><b>Es el chequeo 10 de <c>usync-audit.mjs</c> aplicado a la otra mitad.</b> Aquél ya
    /// cruza esa línea contra los markers del <b>schema</b>; nadie la cruzaba contra la prosa de
    /// la propia guía. Y el fallo que evita es el que §9 describe con todas las letras: un
    /// bloqueo que terminó y nadie movió deja de vigilarse <b>en silencio</b>.</para>
    ///
    /// <para><b>Se lee la línea, no la sección</b>, por la misma razón que allá: §9 explica
    /// bloqueos ya levantados, y contar su historia no puede leerse como declararlos vigentes.
    /// Por eso el marcador se busca <b>fuera</b> de §9.</para>
    /// </remarks>
    [Fact]
    public void La_guia_no_declara_bloqueado_lo_que_ya_no_lo_esta()
    {
        var guia = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        var seccion9 = guia.IndexOf("## 9. Tareas bloqueadas externamente", StringComparison.Ordinal);
        var seccion10 = guia.IndexOf("## 10. Cuando termines una tarea", StringComparison.Ordinal);
        Assert.True(seccion9 >= 0 && seccion10 > seccion9, "No se encontraron §9 y §10: revisar este gate.");

        var sinBloqueos = guia[seccion9..seccion10]
            .Contains("**Bloqueos vigentes:** ninguno.", StringComparison.Ordinal);

        if (!sinBloqueos) return;   // hay bloqueos declarados: nada que cuadrar acá.

        // Fuera de §9, que es donde se cuenta la historia de los que terminaron.
        var fuera = guia[..seccion9] + guia[seccion10..];

        Assert.False(fuera.Contains("externalmente bloqueado", StringComparison.OrdinalIgnoreCase),
            "§9 dice «Bloqueos vigentes: ninguno» y el resto de CLAUDE.md rotula algo como "
            + "«externamente bloqueado». Una de las dos miente, y la de §2 es la que lee primero "
            + "quien no conoce el repo (#74).");
    }

    /// <summary>
    /// Los campos que el CMS EMITE en <c>window.synergos</c> y los que el contrato DOCUMENTA
    /// son los mismos (#88).
    /// </summary>
    /// <remarks>
    /// <para><b>Nada estaba roto cuando esto se escribió</b>, y por eso es mejora y no defecto: las
    /// tres declaraciones coincidían campo por campo. Lo que no había era nada que las obligara a
    /// seguir coincidiendo, y se editan por separado.</para>
    ///
    /// <para><b>Por qué el harness de contratos no puede cazarlo</b>, y no es un defecto suyo: lo
    /// dice él mismo en su cabecera — «NO instancian el bridge desde el CMS», arman un mock de
    /// <c>window.synergos</c> con la forma canónica y verifican los helpers del lado consumidor.
    /// Prueba el mock contra sí mismo. Sus 56 tests siguen verdes si alguien le añade un campo a
    /// <c>HostBridgeMember</c> o le renombra <c>canonicalUrl</c>, y el UI lee <c>undefined</c> en
    /// producción — sin excepción y sin rojo, que es la forma de fallo más cara de este repo.</para>
    ///
    /// <para><b>Se escribió EN VERDE a propósito</b>, que es el argumento de #49 textual: en verde
    /// es gratis, sin excepciones que negociar y sin nadie esperando. Escrito después de que
    /// divergiera, habría que decidir cuál de los tres tiene razón con alguien esperando.</para>
    ///
    /// <para><b>Mira NOMBRES de campo, no tipos, y lo dice para no mentir sobre su alcance.</b>
    /// Cruzar <c>IReadOnlyList&lt;string&gt;</c> contra <c>readonly string[]</c> se puede, y es
    /// mucho más frágil; el fallo que importa —un campo que se añade, se renombra o se va— se caza
    /// con los nombres. Tampoco cuenta <c>t(key, fallback)</c>: es un helper del lado consumidor,
    /// no un campo que el CMS serialice.</para>
    /// </remarks>
    [Fact]
    public void Lo_que_el_bridge_EMITE_y_lo_que_el_contrato_DOCUMENTA_son_lo_mismo()
    {
        var csharp = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Interfaces", "IHostBridgeContextBuilder.cs"));

        // El contrato vive partido a propósito: host-bridge.md remite a i18n-bridge.md para el
        // detalle del i18n, así que se leen los dos como una sola fuente.
        var contrato = string.Join('\n',
            File.ReadAllText(Path.Combine(CarpetaContratos(), "host-bridge.md")),
            File.ReadAllText(Path.Combine(CarpetaContratos(), "i18n-bridge.md")));

        var emitido = Regex.Matches(csharp, @"record HostBridge(\w+)\(([\s\S]*?)\);")
            .ToDictionary(
                m => m.Groups[1].Value,
                m => CamposDelRecord(m.Groups[2].Value).Select(Minuscula).ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        Assert.True(emitido.Count >= 5,
            $"Sólo se leyeron {emitido.Count} records de HostBridge*: el descubrimiento está roto "
            + "y lo de abajo no probaría nada.");

        var documentado = Regex.Matches(contrato, @"interface Synergos(\w+)\s*\{([\s\S]*?)\n\}")
            .ToDictionary(
                m => m.Groups[1].Value,
                m => Regex.Matches(m.Groups[2].Value, @"readonly\s+(\w+)\s*[?:]")
                    .Select(c => c.Groups[1].Value)
                    .ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        // La raíz no es una `interface Synergos*`: es el bloque `declare global` que declara
        // `window.synergos`. Se lee aparte porque su forma es distinta, no porque se exima.
        var raiz = Regex.Match(contrato, @"synergos:\s*\{([\s\S]*?)\n\s*\};");
        Assert.True(raiz.Success,
            "No se encontró el bloque `declare global` que declara `window.synergos` en "
            + "host-bridge.md: cambió de forma y este gate dejó de leer la raíz.");

        documentado["Context"] = Regex.Matches(raiz.Groups[1].Value, @"readonly\s+(\w+)\s*[?:]")
            .Select(c => c.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var malas = new List<string>();

        foreach (var (nombre, campos) in emitido.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (!documentado.TryGetValue(nombre, out var docs))
            {
                malas.Add($"HostBridge{nombre} se emite y NO está documentado en los contratos.");
                continue;
            }

            var soloCodigo = campos.Except(docs).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var soloDoc = docs.Except(campos).OrderBy(x => x, StringComparer.Ordinal).ToList();

            if (soloCodigo.Count > 0)
            {
                malas.Add($"HostBridge{nombre}: emite [{string.Join(", ", soloCodigo)}] que el "
                    + "contrato no documenta — el UI no sabe que existen.");
            }

            if (soloDoc.Count > 0)
            {
                malas.Add($"HostBridge{nombre}: el contrato promete [{string.Join(", ", soloDoc)}] "
                    + "que el CMS no emite — el UI los leerá como `undefined`.");
            }
        }

        Assert.True(malas.Count == 0,
            "La forma de `window.synergos` dejó de coincidir entre lo que el CMS emite y lo que "
            + "`docs/contracts/` documenta. CLAUDE.md §3 llama a esa carpeta «la ÚNICA superficie "
            + "de acople» con el UI, y una divergencia acá no lanza ni pone nada rojo: el UI lee "
            + "`undefined` y pinta un hueco (#88)."
            + Environment.NewLine
            + string.Join(Environment.NewLine, malas));
    }

    /// <summary>
    /// Los nombres de los parámetros de un record posicional.
    /// </summary>
    /// <remarks>
    /// <para><b>Se parte por comas de PRIMER NIVEL, no con una expresión regular.</b> La primera
    /// versión de esto usaba una, y se le escapaba <b>en silencio</b> un campo con valor por
    /// defecto —<c>IReadOnlyDictionary&lt;string, string&gt;? Extras = null</c>—: su lookahead
    /// exigía <c>,</c> o <c>)</c> justo detrás del nombre, y ahí venía un <c>=</c>. El gate se
    /// quedaba <b>verde</b> vigilando un campo menos, que es peor que fallar.</para>
    ///
    /// <para><b>Y no es un caso raro:</b> añadir un campo con valor por defecto es exactamente
    /// cómo se amplía un record sin romper a quien lo construye — es lo que se hizo con
    /// <c>SentWith</c> y con <c>ActedWith</c>. O sea que el hueco estaba justo en la forma que más
    /// se usa para crecer.</para>
    ///
    /// <para>Un genérico anidado trae comas propias (<c>Dictionary&lt;string, string&gt;</c>), así
    /// que la profundidad de <c>&lt;&gt;</c> se lleva a mano. Es más largo que un regex y no se
    /// equivoca.</para>
    /// </remarks>
    private static IEnumerable<string> CamposDelRecord(string parametros)
    {
        var partes = new List<string>();
        var actual = new System.Text.StringBuilder();
        var profundidad = 0;

        foreach (var c in parametros)
        {
            switch (c)
            {
                case '<': profundidad++; actual.Append(c); break;
                case '>': profundidad--; actual.Append(c); break;
                case ',' when profundidad == 0: partes.Add(actual.ToString()); actual.Clear(); break;
                default: actual.Append(c); break;
            }
        }

        partes.Add(actual.ToString());

        foreach (var parte in partes)
        {
            // Se corta el valor por defecto y se toma el ÚLTIMO identificador: lo que queda antes
            // es el tipo, con los genéricos que traiga.
            var sinDefecto = parte.Split('=')[0];
            var nombre = Regex.Match(sinDefecto, @"(\w+)\s*$");

            if (nombre.Success) yield return nombre.Groups[1].Value;
        }
    }

    /// <summary>Primera letra en minúscula: los records van en PascalCase y el JSON en camelCase.</summary>
    private static string Minuscula(string s)
        => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];

    /// <summary>
    /// Los tres sitios donde el host bridge se degrada DEJAN RASTRO (#92).
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita ya estaba puesto, y en el camino que corre.</b>
    /// <c>window.synergos</c> se emite por dos rutas según <c>CspStrictMode</c>: el controller
    /// —cuando está en <c>true</c>— y la vista Razor —cuando está en <c>false</c>, que es el
    /// <b>default</b>—. Las dos caían al mismo contexto degradado; sólo el controller lo
    /// registraba. O sea que la ruta que de verdad corre era la muda.</para>
    ///
    /// <para><b>Y había un tercer catch, más adentro y más silencioso:</b> el del propio builder
    /// al leer el diccionario. Ése es peor que los otros dos porque devuelve un contexto que
    /// <b>parece sano</b> —marca, tema y página intactos— con <c>i18n.keys</c> vacío: el sitio se
    /// queda sin traducciones y todo lo demás sigue igual.</para>
    ///
    /// <para><b>Degradar está bien; degradar en silencio no.</b> Que el bridge no tumbe la página
    /// es correcto y está razonado en el propio fichero — y degrada <b>cerrada</b>, sin otorgar
    /// nada de más. Lo que faltaba es que se sepa: sin una línea de log, un miembro autenticado se
    /// ve anónimo para todos los componentes, el sitio devuelve 200, <c>/health</c> contesta y la
    /// prueba de humo pasa. Es la forma que §11 cataloga como la más cara de este repo.</para>
    ///
    /// <para><b>Por qué los tests no lo vieron:</b> el caso SÍ estaba probado —
    /// <c>Get_BuilderThrows_ReturnsFallbackPayload</c>— pero <b>en la rama que no es el
    /// default</b>, y además sólo afirmaba que devuelve el fallback, no que quede registrado. La
    /// cobertura existía, apuntaba al hermano, y daba la sensación de que el caso estaba cubierto.
    /// </para>
    ///
    /// <para><b>Mira que el catch registre, no CÓMO.</b> Da igual si es <c>LogWarning</c> o
    /// <c>LogError</c> y con qué mensaje; lo que no puede volver es un <c>catch</c> vacío en el
    /// camino del bridge.</para>
    /// </remarks>
    [Theory]
    [InlineData("Synergos.CMS.Web/Views/Shared/_SynergosBridge.cshtml")]
    [InlineData("Synergos.CMS.Web/Services/DefaultHostBridgeContextBuilder.cs")]
    [InlineData("Synergos.CMS.Web/Controllers/SynergosBridgeController.cs")]
    public void Toda_degradacion_del_bridge_deja_rastro(string ruta)
    {
        var fichero = Path.Combine(RepoRoot(), ruta.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(fichero), $"No existe {ruta}: revisar este gate.");

        var codigo = File.ReadAllText(fichero);
        var catches = Regex.Matches(codigo, @"catch\s*\([^)]*\)\s*\{([\s\S]*?)\n(\s*)\}");

        Assert.True(catches.Count > 0,
            $"{ruta} no tiene ningún `catch`. O se quitó la degradación —y entonces hay que "
            + "revisar este gate— o el descubrimiento está roto y lo de abajo no probaría nada.");

        var mudos = catches
            .Where(c => !Regex.IsMatch(c.Groups[1].Value, @"\.Log(Warning|Error|Critical|Information)\("))
            .Select(c => c.Value.Split('\n')[0].Trim())
            .ToList();

        Assert.True(mudos.Count == 0,
            $"{ruta} vuelve a degradarse sin dejar rastro (#92). Que el bridge no tumbe la página "
            + "es correcto —y degrada cerrada—, pero sin una línea de log un miembro autenticado se "
            + "ve anónimo para todos los componentes, el sitio devuelve 200 y la prueba de humo "
            + "pasa. El camino por defecto es la vista Razor, no el controller."
            + Environment.NewLine
            + string.Join(Environment.NewLine, mudos));
    }
}
