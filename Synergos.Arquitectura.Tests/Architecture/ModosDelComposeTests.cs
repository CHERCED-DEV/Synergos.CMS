using System.Reflection;
using System.Text.RegularExpressions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Los interruptores de modo del CMS (<c>Synergos:&lt;X&gt;:Mode</c>): cada palabra que escribe el
/// despliegue la reconoce SU composer (#177), y ninguno cae en silencio con una que no reconoce
/// (#182).
/// </summary>
/// <remarks>
/// <para><b>El defecto, dos veces.</b> <c>compose.prod.yml</c> fijaba <c>Synergos__SearchAnalytics__Mode:
/// Http</c> y el composer sólo reconocía <c>Sessions</c>: cualquier otra palabra caía EN SILENCIO
/// al disco, y <c>Api.Sessions</c> no recibía ni un evento (#177). Se arregló ese interruptor y
/// quedaron catorce con la misma forma: medido componiendo, <c>Htpp</c>, <c>Api</c> en uno que dice
/// <c>Bff</c>, <c>Stub</c> en uno que dice <c>Local</c> o <c>" Api "</c> con espacios cableaban
/// EXACTAMENTE lo mismo que no configurar nada (#182). Hoy los quince leen su modo por UNA pieza,
/// <c>Interruptor</c>, que lanza al cablear nombrando los válidos.</para>
///
/// <para><b>Qué interruptores hay se DERIVA por tres caminos que tienen que coincidir</b> —ninguna
/// lista a mano—: las claves <c>Synergos:…:Mode</c> escritas en los composers, las que los
/// composers le PIDEN a la configuración al componer (<c>ComposicionDelCms.ClavesQueLeen</c>), y
/// los POCO de <c>Application/Configuration</c> con propiedad <c>Mode</c>, por la sección que su
/// composer les enlaza. Así el interruptor dieciséis entra solo al gate, escrito como se escriba.</para>
///
/// <para><b>Qué reconoce cada uno, por dos caminos que tienen que coincidir:</b></para>
/// <list type="number">
///   <item><b>Leído</b>: las palabras que declara cada llamada a <c>Interruptor</c> con esa
///   clave —y cada fila de tabla que la nombra—, con el default leído del POCO por reflexión.</item>
///   <item><b>Ejecutado</b>: se componen de verdad los composers con una palabra inventada y la
///   pieza tiene que negarse nombrando EXACTAMENTE esas palabras, con el default del POCO
///   primero; y con cada palabra reconocida —en cualquier capitalización— tiene que arrancar y,
///   si no es el default, cablear algo distinto que no configurar nada.</item>
/// </list>
///
/// <para><b>Lo que NO ve, dicho para no mentir sobre su alcance</b>: el valor que el operador
/// ponga en el <c>.env</c> del servidor. Cruza lo que el compose trae escrito —el literal, o el
/// default de <c>${VAR:-default}</c>— y lo que <c>.env.example</c> propone. Para lo demás está la
/// pieza: un modo desconocido no arranca, en los quince.</para>
/// </remarks>
[Collection(ComposeExclusivo.Nombre)]
public sealed class ModosDelComposeTests
{
    private sealed record ModoEscrito(string Clave, string Valor, string Origen);

    /// <summary>Una lectura de un modo en la fuente: dónde, qué clave y qué palabras declara.</summary>
    private sealed record Lectura(string Fichero, string Clave, IReadOnlyList<string> Palabras);

    /// <summary>La palabra que nadie escribiría: lo que el gate le da a cada interruptor.</summary>
    private const string Inventada = "Htpp182";

    private static string Raiz() => Proyectos.Raiz();

    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    /// <summary>Los composers, sin comentarios. La pieza misma no cuenta: no lee ninguna clave.</summary>
    private static IReadOnlyList<(string Nombre, string Codigo)> Composers()
        => Directory.EnumerateFiles(Proyectos.Ruta("Synergos.CMS.Web", "Composers"), "*.cs")
            .Where(f => Path.GetFileName(f) != "Interruptor.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), SinComentarios(f)))
            .ToList();

    // ── Lo que el despliegue escribe ────────────────────────────────────────

    private static readonly Regex LineaDeModo = new(
        @"^\s*(?<clave>Synergos__[A-Za-z_]+?__Mode)\s*:\s*(?<valor>.+?)\s*$", RegexOptions.Multiline);

    private static readonly Regex Interpolada = new(@"^\$\{(?<var>[A-Z0-9_]+)(?::-(?<def>[^}]*))?[^}]*\}$");

    /// <summary>
    /// Cada modo que el compose escribe, con el valor que TRAE: el literal, o el default de
    /// <c>${VAR:-default}</c> y lo que <c>.env.example</c> propone para esa variable.
    /// </summary>
    private static IReadOnlyList<ModoEscrito> ModosEscritos()
    {
        var compose = string.Join('\n', File.ReadAllLines(Path.Combine(Raiz(), "compose.prod.yml"))
            .Where(l => !l.TrimStart().StartsWith('#')));

        var ejemplo = File.ReadAllLines(Path.Combine(Raiz(), ".env.example"))
            .Where(l => !l.TrimStart().StartsWith('#') && l.Contains('=', StringComparison.Ordinal))
            .Select(l => l.Split('=', 2))
            .GroupBy(p => p[0].Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last()[1].Trim(), StringComparer.Ordinal);

        var modos = new List<ModoEscrito>();
        foreach (Match m in LineaDeModo.Matches(compose))
        {
            var clave = m.Groups["clave"].Value.Replace("__", ":", StringComparison.Ordinal);
            var crudo = m.Groups["valor"].Value.Trim().Trim('"', '\'');

            var i = Interpolada.Match(crudo);
            if (!i.Success)
            {
                modos.Add(new ModoEscrito(clave, crudo, "compose.prod.yml"));
                continue;
            }

            var variable = i.Groups["var"].Value;
            // Sin default no hay nada que cruzar: el compose no trae palabra y la del .env no se
            // ve desde acá. Se dice en vez de saltarlo.
            modos.Add(new ModoEscrito(clave,
                i.Groups["def"].Success ? i.Groups["def"].Value : string.Empty,
                $"compose.prod.yml (default de {variable})"));

            if (ejemplo.TryGetValue(variable, out var propuesto))
            {
                modos.Add(new ModoEscrito(clave, propuesto, $".env.example ({variable})"));
            }
        }

        return modos;
    }

    // ── Qué interruptores hay: tres caminos ─────────────────────────────────

    private static readonly Regex ClaveDeModo = new(@"""(?<clave>Synergos:[A-Za-z:]+:Mode)""");

    /// <summary>Camino A: toda clave <c>Synergos:…:Mode</c> escrita en el código de un composer.</summary>
    private static IReadOnlySet<string> ClavesEnLaFuente()
        => Composers()
            .SelectMany(c => ClaveDeModo.Matches(c.Codigo).Select(m => m.Groups["clave"].Value))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>Camino B: las claves <c>Synergos:…:Mode</c> que los composers PIDEN al componer.</summary>
    private static IReadOnlySet<string> ClavesQueSePidenAlComponer()
        => ComposicionDelCms.ClavesQueLeen(new Dictionary<string, string?>())
            .Where(k => Regex.IsMatch(k, @"^Synergos:[A-Za-z:]+:Mode$"))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Camino C: cada POCO de configuración con propiedad <c>Mode</c>, por la sección que un
    /// composer le enlaza (<c>Configure&lt;T&gt;(…GetSection("S"))</c>) — <c>S:Mode</c>.
    /// </summary>
    private static IReadOnlyDictionary<string, string> PocosConModo()
    {
        var conModo = typeof(SearchAnalyticsSettings).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("Settings", StringComparison.Ordinal)
                        && t.GetProperty("Mode", BindingFlags.Public | BindingFlags.Instance)?.PropertyType == typeof(string))
            .Select(t => t.Name)
            .ToHashSet(StringComparer.Ordinal);

        var porPoco = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (_, src) in Composers())
        {
            foreach (Match m in Regex.Matches(src,
                         @"Configure<(?<poco>[A-Za-z]+Settings)>\s*\([^;]{0,200}?GetSection\(""(?<seccion>Synergos:[A-Za-z:]+)""\)"))
            {
                if (conModo.Contains(m.Groups["poco"].Value))
                {
                    porPoco[m.Groups["poco"].Value] = m.Groups["seccion"].Value + ":Mode";
                }
            }
        }

        foreach (var sinSeccion in conModo.Where(p => !porPoco.ContainsKey(p)))
        {
            porPoco[sinSeccion] = "(ningún composer enlaza su sección)";
        }

        return porPoco;
    }

    /// <summary>Los interruptores del CMS: lo que escribe la fuente (los otros dos caminos se cruzan aparte).</summary>
    private static IReadOnlyList<string> Interruptores()
        => ClavesEnLaFuente().OrderBy(c => c, StringComparer.Ordinal).ToList();

    // ── Qué reconoce cada uno: LEÍDO ────────────────────────────────────────

    /// <summary>Los argumentos de nivel superior de la llamada que abre en <paramref name="parentesis"/>.</summary>
    private static IReadOnlyList<string> Argumentos(string src, int parentesis)
    {
        var args = new List<string>();
        var nivel = 0;
        var desde = parentesis + 1;
        for (var i = parentesis; i < src.Length; i++)
        {
            var c = src[i];
            if (c == '"')
            {
                i = src.IndexOf('"', i + 1);
                if (i < 0) break;
                continue;
            }
            if (c is '(' or '[') nivel++;
            else if (c is ')' or ']')
            {
                if (--nivel == 0)
                {
                    args.Add(src[desde..i].Trim());
                    return args;
                }
            }
            else if (c == ',' && nivel == 1)
            {
                args.Add(src[desde..i].Trim());
                desde = i + 1;
            }
        }
        return args;
    }

    /// <summary>
    /// La palabra que aporta un argumento: un literal, o el default de un POCO
    /// (<c>new TiendaSettings().Mode</c>) leído por reflexión. Un nombre (<c>porDefecto:</c>) no cuenta.
    /// </summary>
    private static string? Palabra(string argumento)
    {
        var a = Regex.Replace(argumento, @"^[a-zA-Z]+\s*:\s*", string.Empty);
        var literal = Regex.Match(a, @"^""(?<w>[^""]*)""$");
        if (literal.Success) return literal.Groups["w"].Value;

        var poco = Regex.Match(a, @"^new\s+(?<p>[A-Za-z]+Settings)\s*\(\s*\)\s*\.\s*Mode$");
        return poco.Success ? DefaultDe(poco.Groups["p"].Value) : null;
    }

    private static string? DefaultDe(string poco)
    {
        var tipo = typeof(SearchAnalyticsSettings).Assembly.GetTypes().SingleOrDefault(t => t.Name == poco);
        return tipo?.GetProperty("Mode")?.GetValue(Activator.CreateInstance(tipo)) as string;
    }

    /// <summary>
    /// Toda lectura de un modo con la clave escrita: cada llamada a la pieza
    /// (<c>Interruptor.Encendido(cfg, "K", "Bff", new XSettings().Mode)</c> ·
    /// <c>Interruptor.Modo(cfg, "K", porDefecto, "A", "B")</c>) y cada fila de tabla
    /// <c>("K", "Bff", new XSettings().Mode, …)</c>.
    /// </summary>
    private static IReadOnlyList<Lectura> Lecturas()
    {
        var lecturas = new List<Lectura>();
        foreach (var (nombre, src) in Composers())
        {
            foreach (Match m in Regex.Matches(src, @"\bInterruptor\s*\.\s*(?<metodo>Encendido|Modo)\s*\("))
            {
                var args = Argumentos(src, m.Index + m.Length - 1);
                if (args.Count < 4) continue;
                var clave = Regex.Match(args[1], @"^""(?<k>Synergos:[A-Za-z:]+:Mode)""$");
                if (!clave.Success) continue; // la clave llega por variable: la cubre su fila de tabla

                var palabras = m.Groups["metodo"].Value == "Encendido"
                    ? new[] { Palabra(args[3]), Palabra(args[2]) }
                    : args.Skip(2).Select(Palabra).ToArray();
                lecturas.Add(new Lectura(nombre, clave.Groups["k"].Value, palabras.Select(p => p ?? "¿?").ToList()));
            }

            foreach (Match fila in Regex.Matches(src,
                         @"\(\s*""(?<k>Synergos:[A-Za-z:]+:Mode)""\s*,\s*""(?<w>\w+)""\s*,\s*(?<d>new\s+[A-Za-z]+Settings\s*\(\s*\)\s*\.\s*Mode)\s*,"))
            {
                lecturas.Add(new Lectura(nombre, fila.Groups["k"].Value,
                    [Palabra(fila.Groups["d"].Value) ?? "¿?", fila.Groups["w"].Value]));
            }
        }
        return lecturas;
    }

    /// <summary>Camino 1: las palabras que la fuente declara para <paramref name="clave"/>.</summary>
    private static IReadOnlySet<string> ReconocidasLeidas(string clave)
        => Lecturas().Where(l => l.Clave == clave)
            .SelectMany(l => l.Palabras)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// El default que declara el POCO enlazado a la sección de <paramref name="clave"/>, leído
    /// por REFLEXIÓN — el valor de verdad, no el literal del fichero.
    /// </summary>
    private static string? DefaultDelPoco(string clave)
        => PocosConModo().Where(p => p.Value == clave).Select(p => DefaultDe(p.Key)).FirstOrDefault();

    // ── Qué reconoce cada uno: EJECUTADO ────────────────────────────────────

    /// <summary>
    /// Lo que una palabra necesita además para que el CMS arranque con ella: las otras guardas de
    /// arranque, que no son de este gate. Sin esto el gate confundiría «no arranca porque falta la
    /// URL» con «no reconoce la palabra».
    /// </summary>
    private static Dictionary<string, string?> Compania(string clave, string palabra)
    {
        var config = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (clave == "Synergos:Payments:Mode" && palabra.Equals("Api", StringComparison.OrdinalIgnoreCase))
        {
            // ExigirQueNadieOrqueste (#27): con el cobro en la capacidad, nadie orquesta de este lado.
            foreach (var vertical in new[] { "Tienda", "Salud", "Eventos", "Viajes" })
            {
                config[$"Synergos:{vertical}:Mode"] = "Bff";
            }
        }
        if (clave == "Synergos:BundleRegistry:Mode" && palabra.Equals("FileSystem", StringComparison.OrdinalIgnoreCase))
        {
            // RaizDelCdnLocal.Exigir (#132): la carpeta tiene que existir y traer su registry.
            config["Synergos:BundleRegistry:LocalPath"] = CdnDePrueba.Value;
        }
        if (clave == "Synergos:BundleRegistry:Mode" && palabra.Equals("Http", StringComparison.OrdinalIgnoreCase))
        {
            // ExigirUrlPublicaAbsoluta (#56).
            config["Synergos:BundleRegistry:PublicBaseUrl"] = "https://cdn.ejemplo.test";
        }
        return config;
    }

    private static readonly Lazy<string> CdnDePrueba = new(() =>
    {
        var raiz = Path.Combine(Path.GetTempPath(), "synergos-modos-del-compose");
        Directory.CreateDirectory(Path.Combine(raiz, "synergos"));
        File.WriteAllText(Path.Combine(raiz, "synergos", "registry.json"), "{\"elements\":[]}");
        return raiz;
    });

    private static IReadOnlyList<string> HuellaCon(Dictionary<string, string?> config)
        => ComposicionDelCms.Huella(ComposicionDelCms.Componer(config));

    /// <summary>
    /// Compone con <paramref name="clave"/> = <paramref name="valor"/>.
    /// </summary>
    /// <returns><c>null</c> si lo reconoce; si no, por qué.</returns>
    private static string? RechazoEjecutado(string clave, string valor)
    {
        var compania = Compania(clave, valor);
        var sin = HuellaCon(compania);
        IReadOnlyList<string> con;
        try
        {
            con = HuellaCon(new Dictionary<string, string?>(compania) { [clave] = valor });
        }
        catch (InvalidOperationException ex)
        {
            return "el CMS no arranca: " + ex.Message;
        }

        if (!con.SequenceEqual(sin, StringComparer.Ordinal)) return null; // cablea otra cosa: existe

        var porDefecto = DefaultDelPoco(clave);
        return string.Equals(valor, porDefecto, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"cablea exactamente lo mismo que sin configurar ({porDefecto ?? "sin POCO"}): nadie lee esa palabra y cae en silencio al camino por defecto";
    }

    // ── Que el gate VE ──────────────────────────────────────────────────────

    [Fact]
    public void El_descubrimiento_ve_los_interruptores_por_tres_caminos_que_coinciden()
    {
        var fuente = ClavesEnLaFuente();
        var pedidas = ClavesQueSePidenAlComponer();
        var pocos = PocosConModo();

        // Un piso, no una cifra: con menos que esto el descubrimiento está roto y los demás facts
        // pasarían en verde sobre nada. La cifra de verdad es la que dan los tres caminos.
        Assert.True(fuente.Count >= 15 && ModosEscritos().Count >= 15,
            $"Se leyeron {fuente.Count} claves de los composers y {ModosEscritos().Count} modos del compose: el "
            + "descubrimiento está roto.");

        Assert.True(fuente.SetEquals(pedidas),
            "Las claves Synergos:…:Mode escritas en los composers y las que piden al componer no coinciden.\n"
            + "  sólo escritas: " + string.Join(", ", fuente.Except(pedidas)) + "\n"
            + "  sólo pedidas: " + string.Join(", ", pedidas.Except(fuente))
            + "\nUna clave que se pide armada con una variable no se ve en la fuente; una escrita que no se "
            + "pide al componer se lee DESPUÉS, en una fábrica: las dos se le escapan a una de las mitades.");

        var sinInterruptor = pocos.Where(p => !fuente.Contains(p.Value))
            .Select(p => $"{p.Key} → {p.Value}").ToList();
        var sinPoco = fuente.Where(c => !pocos.Values.Contains(c)).ToList();
        Assert.True(sinInterruptor.Count == 0 && sinPoco.Count == 0,
            "Los POCO con Mode y los interruptores no coinciden.\n  POCO sin interruptor: "
            + string.Join(", ", sinInterruptor) + "\n  interruptor sin POCO: " + string.Join(", ", sinPoco)
            + "\nEl default de un modo lo declara su POCO: sin él, la pieza no tiene de dónde sacarlo.");

        // Y el camino LEÍDO tiene que ver algo de cada clave.
        var ciegas = fuente.Where(c => ReconocidasLeidas(c).Count < 2).ToList();
        Assert.True(ciegas.Count == 0,
            "De estas claves no se deriva un default Y una palabra más: " + string.Join(", ", ciegas)
            + ". O el composer las lee sin la pieza Interruptor, o este gate no entiende cómo la llama.");
    }

    // ── La regla ────────────────────────────────────────────────────────────

    [Fact]
    public void Todo_modo_que_escribe_el_compose_lo_lee_un_composer_y_al_reves()
    {
        // Un modo escrito que nadie lee no hace nada y nadie se entera. Uno leído que el
        // despliegue no escribe no se puede encender en el servidor sin editar el compose: el
        // seam queda en su default para siempre — la forma de "Toda variable documentada la
        // CONSUME alguien" (ComposeStackTests), mirada desde el otro lado.
        var escritas = ModosEscritos().Select(e => e.Clave).ToHashSet(StringComparer.Ordinal);
        var leidas = ClavesEnLaFuente();

        var nadieLee = escritas.Where(c => !leidas.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();
        var nadieEscribe = leidas.Where(c => !escritas.Contains(c)).OrderBy(c => c, StringComparer.Ordinal).ToList();

        Assert.True(nadieLee.Count == 0,
            "compose.prod.yml escribe modos que ningún composer lee: " + string.Join(", ", nadieLee) + ".");
        Assert.True(nadieEscribe.Count == 0,
            "Estos modos los lee un composer y el despliegue no los escribe: " + string.Join(", ", nadieEscribe)
            + ". Sin su línea en tools/compose-gen.mjs —con el default EN PROCESO— no se pueden encender "
            + "en el servidor.");
    }

    [Fact]
    public void El_compose_y_el_env_example_escriben_modos_que_su_composer_reconoce()
    {
        var malos = new List<string>();
        foreach (var escrito in ModosEscritos())
        {
            if (escrito.Valor.Length == 0)
            {
                malos.Add($"{escrito.Clave} en {escrito.Origen}: no trae palabra, así que no se puede cruzar");
                continue;
            }

            var leidas = ReconocidasLeidas(escrito.Clave);
            var porLectura = leidas.Contains(escrito.Valor);
            var rechazo = RechazoEjecutado(escrito.Clave, escrito.Valor);

            if (porLectura && rechazo is null) continue;

            var reconoce = string.Join(", ", leidas.OrderBy(p => p, StringComparer.Ordinal));
            malos.Add(porLectura == (rechazo is null)
                ? $"{escrito.Clave}={escrito.Valor} en {escrito.Origen}: su composer reconoce {reconoce}; {rechazo}"
                : $"{escrito.Clave}={escrito.Valor} en {escrito.Origen}: los dos caminos NO coinciden "
                  + $"(leído: {(porLectura ? "sí" : "no")} de [{reconoce}]; ejecutado: {rechazo ?? "sí"}). "
                  + "Revisar este gate antes que el compose.");
        }

        Assert.True(malos.Count == 0,
            "El despliegue escribe modos que su composer no reconoce:\n  " + string.Join("\n  ", malos)
            + "\nEs el #177: compose.prod.yml decía Http, el composer entendía Sessions y la analítica "
            + "caía en silencio al disco. Se arregla en tools/compose-gen.mjs —el compose se genera— "
            + "o en el composer, pero con UNA palabra: la del molde es Api (capacidad) o Bff (orquestador).");
    }

    [Fact]
    public void Ningun_interruptor_cae_en_silencio_con_una_palabra_que_no_conoce()
    {
        var malos = new List<string>();
        foreach (var clave in Interruptores())
        {
            ModoDesconocidoException? rechazo = null;
            try
            {
                HuellaCon(new Dictionary<string, string?> { [clave] = Inventada });
            }
            catch (ModoDesconocidoException ex) when (ex.Clave == clave)
            {
                rechazo = ex;
            }
            catch (InvalidOperationException ex)
            {
                malos.Add($"{clave}={Inventada}: no arranca, pero no por la pieza — {ex.Message}");
                continue;
            }

            if (rechazo is null)
            {
                malos.Add($"{clave}={Inventada}: el CMS ARRANCA. Cae en silencio a otro camino — "
                          + "léelo con Interruptor.Encendido/Modo (Composers/Interruptor.cs), no a mano.");
                continue;
            }

            // Ejecutado contra leído: la pieza nombra exactamente lo que la fuente declara…
            var leidas = ReconocidasLeidas(clave);
            if (!leidas.SetEquals(rechazo.Validos))
            {
                malos.Add($"{clave}: la pieza nombra [{string.Join(", ", rechazo.Validos)}] y la fuente declara "
                          + $"[{string.Join(", ", leidas)}]. Dos lecturas del mismo interruptor no reconocen lo mismo.");
            }

            // …y su default es el del POCO, que es lo que recibe el servicio sin la clave.
            var poco = DefaultDelPoco(clave);
            if (!string.Equals(rechazo.Validos[0], poco, StringComparison.Ordinal))
            {
                malos.Add($"{clave}: la pieza toma «{rechazo.Validos[0]}» por default y el POCO dice «{poco}».");
            }

            // Y el mensaje le sirve al operador: la clave, lo que puso y lo que podía poner.
            if (!rechazo.Message.Contains(clave, StringComparison.Ordinal)
                || !rechazo.Message.Contains($"'{Inventada}'", StringComparison.Ordinal)
                || rechazo.Validos.Any(v => !rechazo.Message.Contains(v, StringComparison.Ordinal)))
            {
                malos.Add($"{clave}: el rechazo no nombra la clave, la palabra y las válidas.");
            }
        }

        Assert.True(malos.Count == 0,
            "Interruptores que no se niegan bien con una palabra que no conocen:\n  " + string.Join("\n  ", malos)
            + "\nEs el #182: una errata en el .env deja el vertical en su Stub o en el almacén local SIN AVISAR, "
            + "y el CMS arranca y se ve bien.");
    }

    [Fact]
    public void Toda_palabra_que_reconoce_un_interruptor_arranca_y_hace_algo()
    {
        var malos = new List<string>();
        foreach (var clave in Interruptores())
        {
            var porDefecto = DefaultDelPoco(clave);
            foreach (var palabra in ReconocidasLeidas(clave))
            {
                var compania = Compania(clave, palabra);
                var sin = HuellaCon(compania);

                IReadOnlyList<string> con;
                try
                {
                    con = HuellaCon(new Dictionary<string, string?>(compania) { [clave] = palabra });
                }
                catch (InvalidOperationException ex)
                {
                    malos.Add($"{clave}={palabra}: no arranca — {ex.Message}");
                    continue;
                }

                var esElDefault = string.Equals(palabra, porDefecto, StringComparison.OrdinalIgnoreCase);
                if (esElDefault != con.SequenceEqual(sin, StringComparer.Ordinal))
                {
                    malos.Add(esElDefault
                        ? $"{clave}={palabra}: es el default y cablea distinto que no configurar nada"
                        : $"{clave}={palabra}: se reconoce y cablea LO MISMO que no configurar nada — una palabra muerta");
                }

                // Mayúsculas y espacios no cuentan: lo que ya funcionaba sigue funcionando.
                foreach (var variante in new[] { palabra.ToLowerInvariant(), $" {palabra.ToUpperInvariant()} " })
                {
                    var otra = HuellaCon(new Dictionary<string, string?>(compania) { [clave] = variante });
                    if (!otra.SequenceEqual(con, StringComparer.Ordinal))
                    {
                        malos.Add($"{clave}='{variante}': no cablea lo mismo que '{palabra}'");
                    }
                }
            }
        }

        Assert.True(malos.Count == 0, "Palabras reconocidas que no se portan:\n  " + string.Join("\n  ", malos));
    }

    [Fact]
    public void Ningun_composer_lee_un_modo_a_mano()
    {
        // El diente de FUENTE del #182, para lo que el de arriba no alcanza: una lectura en una
        // fábrica corre al resolver, no al componer, y ahí la palabra inventada no la toca.
        var malos = new List<string>();
        foreach (var (nombre, src) in Composers())
        {
            foreach (var (linea, n) in src.Split('\n').Select((l, i) => (l, i + 1)))
            {
                var aMano = Regex.IsMatch(linea, @"\[\s*""Synergos:[A-Za-z:]+:Mode""\s*\]")
                            || (Regex.IsMatch(Regex.Replace(linea, @"new\s+[A-Za-z]+Settings\s*\(\s*\)\s*\.\s*Mode\b", ""), @"\.\s*Mode\b")
                                && !linea.Contains("Interruptor.", StringComparison.Ordinal));
                if (aMano) malos.Add($"{nombre}:{n}: {linea.Trim()}");
            }
        }

        Assert.True(malos.Count == 0,
            "Estas líneas leen un modo sin la pieza:\n  " + string.Join("\n  ", malos)
            + "\nUn string.Equals a mano cae en silencio con una palabra desconocida (#182). Va por "
            + "Interruptor.Encendido(config, clave, encendido, new XSettings().Mode) o Interruptor.Modo.");
    }

    [Fact]
    public void La_analitica_con_Api_cablea_el_cliente_de_la_capacidad()
    {
        // El camino bueno del #177, con los composers de verdad.
        var conApi = ComposicionDelCms.Componer(
            new Dictionary<string, string?> { ["Synergos:SearchAnalytics:Mode"] = "Api" });
        Assert.Contains(conApi, d => ComposicionDelCms.Entrega(d) == "HttpSearchAnalyticsStore");

        // Y las dos palabras que lo rompieron —la del compose viejo y la del composer viejo— no
        // arrancan.
        foreach (var palabra in new[] { "Http", "Sessions" })
        {
            var ex = Assert.Throws<ModoDesconocidoException>(() => ComposicionDelCms.Componer(
                new Dictionary<string, string?> { ["Synergos:SearchAnalytics:Mode"] = palabra }));
            Assert.Equal(["FileSystem", "Api"], ex.Validos);
        }
    }
}
