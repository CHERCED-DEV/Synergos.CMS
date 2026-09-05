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
                m => Regex.Matches(m.Groups[2].Value, @"(?:^|,)\s*[\w<>,?\s.]+?\s(\w+)(?=\s*[,)]|\s*$)")
                    .Select(c => Minuscula(c.Groups[1].Value))
                    .ToHashSet(StringComparer.Ordinal),
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

    /// <summary>Primera letra en minúscula: los records van en PascalCase y el JSON en camelCase.</summary>
    private static string Minuscula(string s)
        => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
