using System.Text.RegularExpressions;
using Synergos.CMS.Application.Configuration;
using Synergos.CMS.Web.Composers;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Cada <c>Synergos__&lt;Seam&gt;__Mode</c> que escribe el despliegue es un modo que SU composer
/// reconoce (#177).
/// </summary>
/// <remarks>
/// <para><b>El defecto.</b> <c>compose.prod.yml</c> fijaba <c>Synergos__SearchAnalytics__Mode:
/// Http</c> y el composer sólo reconocía <c>Sessions</c>: cualquier otra palabra caía EN SILENCIO
/// al disco. En producción la analítica de búsqueda se guardaba en el contenedor del CMS y
/// <c>Api.Sessions</c> —cuyo único consumidor es éste— no recibía ni un evento, sin un aviso.
/// <c>ComposeStackTests</c> comprobaba que el compose estuviera al día con su generador, no que
/// lo que el generador escribe lo entienda alguien; <c>CapacidadesConectadasTests</c> cuenta la
/// conexión por código. Ninguno cruzaba las dos mitades.</para>
///
/// <para><b>Lo que el composer reconoce se DERIVA del código, por dos caminos que tienen que
/// coincidir</b> —ninguna lista a mano—:</para>
/// <list type="number">
///   <item><b>Leído</b>: las palabras contra las que el composer compara esa clave —directo, a
///   través de una variable, de un ayudante o de una tabla— más el default que declara su POCO,
///   leído por reflexión.</item>
///   <item><b>Ejecutado</b>: se componen de verdad los composers con esa clave en ese valor. Si
///   lanza, no lo reconoce. Si cablea algo distinto que el default, lo reconoce. Si cablea lo
///   mismo, lo reconoce SÓLO si es la palabra del default — cualquier otra es el defecto #177:
///   una palabra que nadie lee y que cae en silencio al camino en proceso.</item>
/// </list>
///
/// <para><b>Lo que NO ve, dicho para no mentir sobre su alcance</b>: el valor que el operador
/// ponga en el <c>.env</c> del servidor. Cruza lo que el compose trae escrito —el literal, o el
/// default de <c>${VAR:-default}</c>— y lo que <c>.env.example</c> propone. Para lo demás está el
/// otro diente del #177: un modo desconocido no arranca, en el seam que ya lo valida.</para>
/// </remarks>
public sealed class ModosDelComposeTests
{
    private sealed record ModoEscrito(string Clave, string Valor, string Origen);

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

    private static IReadOnlyList<string> Composers()
        => Directory.EnumerateFiles(Proyectos.Ruta("Synergos.CMS.Web", "Composers"), "*.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(SinComentarios)
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

    // ── Lo que el composer reconoce: LEÍDO ──────────────────────────────────

    private static readonly Regex ClaveLeida = new(@"Config\[""(?<clave>Synergos:[A-Za-z:]+:Mode)""\]");

    /// <summary>Las claves <c>Synergos:…:Mode</c> que algún composer lee.</summary>
    private static IReadOnlySet<string> ClavesQueLeenLosComposers()
        => Composers()
            .SelectMany(c => ClaveLeida.Matches(c).Select(m => m.Groups["clave"].Value))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>El cuerpo de un método del fichero, por su nombre.</summary>
    private static string Cuerpo(string src, string metodo)
    {
        var m = Regex.Match(src, @"\b(?:static|private|public|internal)[^;{=]{0,160}?\b" + Regex.Escape(metodo) + @"\s*\(");
        if (!m.Success) return string.Empty;
        var llave = src.IndexOf('{', m.Index);
        var flecha = src.IndexOf("=>", m.Index, StringComparison.Ordinal);
        if (flecha >= 0 && (llave < 0 || flecha < llave))
        {
            var fin = src.IndexOf(';', flecha);
            return fin < 0 ? string.Empty : src[m.Index..fin];
        }
        if (llave < 0) return string.Empty;
        var d = 0;
        for (var k = llave; k < src.Length; k++)
        {
            if (src[k] == '{') d++;
            else if (src[k] == '}' && --d == 0) return src[m.Index..(k + 1)];
        }
        return string.Empty;
    }

    /// <summary>Las palabras con las que un texto compara (<c>string.Equals(x, "Palabra"</c>).</summary>
    private static IEnumerable<string> Comparaciones(string texto, string sujeto)
        => Regex.Matches(texto, @"string\.Equals\(\s*" + sujeto + @"\s*,\s*""(?<w>\w+)""")
            .Select(m => m.Groups["w"].Value);

    /// <summary>
    /// Camino 1: las palabras contra las que los composers comparan <paramref name="clave"/>, más
    /// el default que declara su POCO.
    /// </summary>
    private static IReadOnlySet<string> ReconocidasLeidas(string clave)
    {
        var palabras = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var k = Regex.Escape(clave);

        foreach (var src in Composers())
        {
            // Directo, o a través de un ayudante: `string.Equals(builder.Config["K"], "X"` y
            // `string.Equals(Ayudante(builder.Config["K"]), "X"`.
            foreach (Match m in Regex.Matches(src, @"\[""" + k + @"""\]\s*\)*\s*,\s*""(?<w>\w+)"""))
            {
                palabras.Add(m.Groups["w"].Value);
            }

            // En una tabla: `("K", "X", …)`.
            foreach (Match m in Regex.Matches(src, @"\(\s*""" + k + @"""\s*,\s*""(?<w>\w+)"""))
            {
                palabras.Add(m.Groups["w"].Value);
            }

            // A través de una variable: `var v = …["K"] ?? "D";` y luego `string.Equals(v, "X"`.
            foreach (Match m in Regex.Matches(src, @"var\s+(?<v>\w+)\s*=\s*[^;]*\[""" + k + @"""\][^;]*;"))
            {
                palabras.UnionWith(Comparaciones(src, Regex.Escape(m.Groups["v"].Value)));
                var porDefecto = Regex.Match(m.Value, @"\?\?\s*""(?<w>\w+)""");
                if (porDefecto.Success) palabras.Add(porDefecto.Groups["w"].Value);
            }

            // Dentro de un ayudante que recibe el valor: `Ayudante(builder.Config["K"])`. Se
            // leen sus comparaciones y la tabla de modos que nombre (`X = ["A", "B"]`).
            foreach (Match m in Regex.Matches(src, @"\b(?<h>[A-Z]\w+)\s*\(\s*(?:builder\.)?Config\[""" + k + @"""\]\s*\)"))
            {
                var cuerpo = Cuerpo(src, m.Groups["h"].Value);
                palabras.UnionWith(Regex.Matches(cuerpo, @"string\.Equals\(\s*\w+\s*,\s*""(?<w>\w+)""")
                    .Select(x => x.Groups["w"].Value));
                foreach (Match campo in Regex.Matches(cuerpo, @"\b(?<f>[A-Z]\w+)\b"))
                {
                    var tabla = Regex.Match(src,
                        @"\b" + Regex.Escape(campo.Groups["f"].Value) + @"\s*=\s*\[(?<items>\s*""\w+""(?:\s*,\s*""\w+"")*\s*)\]");
                    if (tabla.Success)
                    {
                        palabras.UnionWith(Regex.Matches(tabla.Groups["items"].Value, @"""(?<w>\w+)""")
                            .Select(x => x.Groups["w"].Value));
                    }
                }
            }
        }

        var poco = DefaultDelPoco(clave);
        if (poco is not null) palabras.Add(poco);

        return palabras;
    }

    /// <summary>
    /// El default que declara el POCO enlazado a la sección de <paramref name="clave"/>, leído
    /// por REFLEXIÓN — el valor de verdad, no el literal del fichero.
    /// </summary>
    private static string? DefaultDelPoco(string clave)
    {
        var seccion = clave[..^":Mode".Length];
        foreach (var src in Composers())
        {
            var m = Regex.Match(src,
                @"Configure<(?<poco>[A-Za-z]+Settings)>\s*\([^;]{0,200}?GetSection\(""" + Regex.Escape(seccion) + @"""\)");
            if (!m.Success) continue;

            var tipo = typeof(SearchAnalyticsSettings).Assembly.GetTypes()
                .SingleOrDefault(t => t.Name == m.Groups["poco"].Value);
            var modo = tipo?.GetProperty("Mode")?.GetValue(Activator.CreateInstance(tipo)) as string;
            if (modo is not null) return modo;
        }
        return null;
    }

    // ── Lo que el composer reconoce: EJECUTADO ──────────────────────────────

    /// <summary>
    /// Camino 2: compone de verdad con <paramref name="clave"/> = <paramref name="valor"/>.
    /// </summary>
    /// <returns><c>null</c> si lo reconoce; si no, por qué.</returns>
    private static string? RechazoEjecutado(string clave, string valor, IReadOnlyList<string> huellaPorDefecto)
    {
        IReadOnlyList<string> huella;
        try
        {
            huella = ComposicionDelCms.Huella(ComposicionDelCms.Componer(
                new Dictionary<string, string?> { [clave] = valor }));
        }
        catch (InvalidOperationException ex)
        {
            return "el CMS no arranca: " + ex.Message;
        }

        if (!huella.SequenceEqual(huellaPorDefecto, StringComparer.Ordinal))
        {
            return null; // cablea otra cosa: es un modo que existe.
        }

        var porDefecto = DefaultDelPoco(clave);
        return string.Equals(valor, porDefecto, StringComparison.OrdinalIgnoreCase)
            ? null
            : $"cablea exactamente lo mismo que sin configurar ({porDefecto ?? "sin POCO"}): nadie lee esa palabra y cae en silencio al camino por defecto";
    }

    // ── Que el gate VE ──────────────────────────────────────────────────────

    [Fact]
    public void El_descubrimiento_ve_los_modos_del_compose_y_de_los_composers()
    {
        var escritos = ModosEscritos();
        var leidas = ClavesQueLeenLosComposers();

        Assert.True(escritos.Select(e => e.Clave).Distinct(StringComparer.Ordinal).Count() >= 12 && leidas.Count >= 12,
            $"Se leyeron {escritos.Count} modos del compose y {leidas.Count} claves de los composers: el "
            + "descubrimiento está roto y los demás facts pasarían en verde sobre nada.");

        // El caso que lo originó tiene que estar a la vista, por los dos lados.
        Assert.Contains(escritos, e => e.Clave == "Synergos:SearchAnalytics:Mode");
        Assert.Contains("Synergos:SearchAnalytics:Mode", leidas);

        // Y el camino LEÍDO tiene que ver algo de cada clave: una clave de la que no se deriva
        // ninguna palabra es un cruce que siempre falla, o —peor— uno que alguien afloja.
        var ciegas = leidas.Where(c => ReconocidasLeidas(c).Count == 0).ToList();
        Assert.True(ciegas.Count == 0,
            "De estas claves no se deriva ninguna palabra reconocida: " + string.Join(", ", ciegas)
            + ". El composer las compara de una forma que este gate no lee: enseñársela antes de fiarse.");
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
        var leidas = ClavesQueLeenLosComposers();

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
    public void El_compose_escribe_modos_que_su_composer_reconoce()
    {
        var huellaPorDefecto = ComposicionDelCms.Huella(
            ComposicionDelCms.Componer(new Dictionary<string, string?>()));

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
            var rechazo = RechazoEjecutado(escrito.Clave, escrito.Valor, huellaPorDefecto);

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
    public void Un_modo_de_analitica_desconocido_no_arranca()
    {
        // El otro diente del #177, con los composers de verdad: la palabra vieja y la del compose
        // viejo tienen que TUMBAR el cableado, nombrando las válidas — no caer al disco.
        foreach (var palabra in new[] { "Http", "Sessions" })
        {
            var ex = Assert.Throws<InvalidOperationException>(() => ComposicionDelCms.Componer(
                new Dictionary<string, string?> { ["Synergos:SearchAnalytics:Mode"] = palabra }));

            Assert.Contains($"'{palabra}'", ex.Message, StringComparison.Ordinal);
            foreach (var valido in SeamComposer.ModosDeAnaliticaDeBusqueda)
            {
                Assert.Contains(valido, ex.Message, StringComparison.Ordinal);
            }
        }

        // Y el camino bueno cablea el cliente de la capacidad.
        var conApi = ComposicionDelCms.Componer(
            new Dictionary<string, string?> { ["Synergos:SearchAnalytics:Mode"] = "Api" });
        Assert.Contains(conApi, d => ComposicionDelCms.Entrega(d) == "HttpSearchAnalyticsStore");
    }
}
