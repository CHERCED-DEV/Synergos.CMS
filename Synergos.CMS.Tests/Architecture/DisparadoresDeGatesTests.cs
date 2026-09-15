using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Un gate se dispara cuando cambia lo que LEE (#128).
/// </summary>
/// <remarks>
/// <para><b>Qué pasó.</b> `design-gates.yml` corre cuatro gates, y dos de ellos —G-6
/// (<c>contract-keys.mjs</c>) y G-7 (<c>contract-bodies.mjs</c>)— leen
/// <c>Synergos.CMS.Web/Controllers/</c>. Su filtro <c>paths:</c> no nombraba esa carpeta, así que
/// un PR que tocaba <b>sólo un controller</b> —que es exactamente donde vive la deriva de
/// contrato— no disparaba ninguno de los dos.</para>
///
/// <para><b>Y no falla a la vista</b>: el workflow no se pone rojo, se <i>salta</i>, y en la lista
/// de checks un «skipped» se lee igual que un «no aplica». Los siete defectos de contrato de
/// #102–#110 vivían todos en un controller.</para>
///
/// <para><b>Por qué hay gate y no bastaba con añadir la ruta.</b> El filtro es una lista a mano, y
/// este repo ya tiene tres casos medidos de listas a mano que se desincronizaron en silencio: las
/// capacidades que verifican tokens en <c>compose-gen.mjs</c> (decía dos, eran cuatro), el
/// <c>TIER_BY_NAME</c> del repo hermano, y «los seis de <c>Synergos.Shared</c>». Añadir la ruta
/// arregla hoy; derivarla arregla la próxima.</para>
///
/// <para><b>Se deriva la LISTA, no la cifra</b> (<c>feedback_a_named_list_beats_a_count</c>), y se
/// cruza <b>en los dos sentidos</b>: una carpeta que un gate lee y el filtro no nombra rompe el
/// build, y una que el filtro nombra y ningún gate lee, también — porque una ruta que sobra
/// convierte el filtro en ruido y deja de leerse.</para>
///
/// <para><b>Lo que este gate NO ve, dicho para no insinuar cobertura:</b> un script que construya
/// su ruta en tiempo de ejecución desde una variable, y los scripts que viven en el repo HERMANO
/// —<c>validate-cms-contracts.mjs</c> y <c>sync-tokens.mjs</c>, que el workflow corre tras hacer
/// checkout de <c>Synergos.UI</c>—. Esos no están en este disco, así que acá no hay nada que
/// derivar; se nombran en la excepción con su razón.</para>
/// </remarks>
public sealed class DisparadoresDeGatesTests
{
    private const string Workflow = ".github/workflows/design-gates.yml";

    /// <summary>
    /// Scripts que el workflow corre y que NO viven en este repo, con su razón.
    /// </summary>
    /// <remarks>
    /// Van con nombre y motivo porque una excepción sin razón al lado deja de leerse. Si alguno
    /// apareciera en este repo, el gate lo trataría como los demás.
    /// </remarks>
    private static readonly Dictionary<string, string> DelRepoHermano = new(StringComparer.Ordinal)
    {
        ["validate-cms-contracts.mjs"] = "vive en Synergos.UI; el workflow lo corre tras el checkout del hermano",
        ["sync-tokens.mjs"] = "vive en Synergos.UI/platforms/angular/tools",
    };

    /// <summary>
    /// Qué carpetas de ESTE repo leen los scripts del hermano.
    /// </summary>
    /// <remarks>
    /// <para><b>Es una lista a mano, y va dicho.</b> Los otros scripts se miden leyéndolos; éstos
    /// no están en este disco, así que no hay nada que derivar sin clonar el hermano — y un gate
    /// que necesita la red para correr deja de correrse.</para>
    ///
    /// <para>Lo destapó el propio gate: marcó <c>uSync/**</c> como ruta que sobra, porque quien la
    /// lee es <c>validate-cms-contracts.mjs</c> y yo lo estaba saltando. Sin esta declaración la
    /// salida honesta habría sido quitar el segundo diente — y entonces una ruta muerta en el
    /// filtro no la vería nadie.</para>
    ///
    /// <para><b>El riesgo que queda</b>: si el script del hermano deja de leer uSync, esto se queda
    /// diciendo que sí. Es el precio de no clonar, y es menor que el de no vigilar — pero es real,
    /// así que se escribe en vez de insinuar que el cruce es completo.</para>
    /// </remarks>
    private static readonly Dictionary<string, string> LeeDelHermano = new(StringComparer.Ordinal)
    {
        ["uSync"] = "validate-cms-contracts.mjs cruza uSync/v9/ContentTypes/ contra el registry del hermano",
        ["wwwroot"] = "sync-tokens.mjs --check compara los tokens contra wwwroot/css/syn-tokens.css",
    };

    private static string RaizDelRepo([System.Runtime.CompilerServices.CallerFilePath] string f = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(f)!, "..", ".."));

    /// <summary>Las rutas del filtro `paths:`, de los dos bloques (pull_request y push).</summary>
    private static IReadOnlyCollection<string> RutasDelFiltro(string yaml)
        => Regex.Matches(yaml, @"^\s*-\s*'([^']+)'\s*$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .Where(v => v.Contains('/') || v.EndsWith(".mjs", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>Los `node tools/x.mjs` que el workflow ejecuta.</summary>
    private static IReadOnlyCollection<string> ScriptsQueCorre(string yaml)
        => Regex.Matches(yaml, @"node\s+(?:\$\{?\w+\}?/)?(?:[\w./-]*?/)?tools/([\w.-]+\.mjs)")
            .Select(m => m.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Las carpetas bajo <c>Synergos.CMS.Web/</c> que un script lee.
    /// </summary>
    /// <remarks>
    /// Se parsea sobre la fuente SIN comentarios: este fichero y los scripts nombran carpetas para
    /// explicar qué pasó, y contarlas mediría la prosa
    /// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </remarks>
    private static IReadOnlyCollection<string> CarpetasQueLee(string codigo)
    {
        var sinComentarios = Regex.Replace(codigo, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        sinComentarios = Regex.Replace(sinComentarios, @"//.*?$", string.Empty, RegexOptions.Multiline);

        return Regex.Matches(sinComentarios, @"'Synergos\.CMS\.Web'\s*,\s*'([A-Za-z0-9_.-]+)'")
            .Select(m => m.Groups[1].Value)
            .Concat(Regex.Matches(sinComentarios, @"Synergos\.CMS\.Web/([A-Za-z0-9_.-]+)/")
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public void Todo_lo_que_un_gate_LEE_dispara_el_workflow()
    {
        var raiz = RaizDelRepo();
        var yaml = File.ReadAllText(Path.Combine(raiz, Workflow));
        var filtro = RutasDelFiltro(yaml);
        var scripts = ScriptsQueCorre(yaml);

        // Red de seguridad: si el descubrimiento deja de ver, las listas salen vacías y el cruce
        // pasaría en verde sin mirar nada.
        Assert.True(scripts.Count >= 3, $"El workflow debería correr varios gates; se vieron {scripts.Count}.");
        Assert.True(filtro.Count >= 5, $"El filtro debería tener varias rutas; se vieron {filtro.Count}.");

        var faltan = new List<string>();

        foreach (var script in scripts)
        {
            if (DelRepoHermano.ContainsKey(script)) continue;

            var ruta = Path.Combine(raiz, "tools", script);
            Assert.True(File.Exists(ruta),
                $"El workflow corre tools/{script} y no está en el disco. Si vive en el repo "
                + "hermano, va en DelRepoHermano con su razón.");

            // El propio script tiene que disparar su gate: tocarlo y que no corra es la misma
            // familia de defecto.
            if (!filtro.Any(r => r.Contains(script, StringComparison.Ordinal)))
            {
                faltan.Add($"tools/{script} — el gate se corre y tocarlo no lo dispara");
            }

            foreach (var carpeta in CarpetasQueLee(File.ReadAllText(ruta)))
            {
                var esperado = $"Synergos.CMS.Web/{carpeta}/";
                if (!filtro.Any(r => r.StartsWith(esperado, StringComparison.Ordinal)))
                {
                    faltan.Add($"Synergos.CMS.Web/{carpeta}/** — lo lee tools/{script}");
                }
            }
        }

        Assert.True(faltan.Count == 0,
            "design-gates.yml corre gates que leen esto y su filtro `paths:` no lo nombra, así que "
            + "un PR que toque SÓLO eso no los dispara — y el workflow no se pone rojo: se SALTA, "
            + "que en la lista de checks se lee igual que «no aplica» (#128):\n  "
            + string.Join("\n  ", faltan.Distinct()));
    }

    [Fact]
    public void Y_el_filtro_no_nombra_rutas_que_ningun_gate_lee()
    {
        var raiz = RaizDelRepo();
        var yaml = File.ReadAllText(Path.Combine(raiz, Workflow));
        var scripts = ScriptsQueCorre(yaml);

        var leidas = scripts
            .Where(s => !DelRepoHermano.ContainsKey(s))
            .Select(s => Path.Combine(raiz, "tools", s))
            .Where(File.Exists)
            .SelectMany(p => CarpetasQueLee(File.ReadAllText(p)))
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(leidas);

        var sobran = RutasDelFiltro(yaml)
            .Where(r => r.StartsWith("Synergos.CMS.Web/", StringComparison.Ordinal))
            .Select(r => r["Synergos.CMS.Web/".Length..].Split('/')[0])
            .Distinct(StringComparer.Ordinal)
            .Where(c => !leidas.Contains(c) && !LeeDelHermano.ContainsKey(c))
            .ToList();

        Assert.True(sobran.Count == 0,
            "El filtro `paths:` nombra rutas que ningún gate de este workflow lee. Una ruta que "
            + "sobra convierte el filtro en ruido y deja de leerse — y el día que haga falta "
            + "quitar una de verdad, nadie distingue cuál:\n  "
            + string.Join("\n  ", sobran.Select(c => $"Synergos.CMS.Web/{c}/**")));
    }
}
