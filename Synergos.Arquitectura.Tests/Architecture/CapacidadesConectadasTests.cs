using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Con cuántos servicios habla el CMS, <b>derivado del código</b> y cruzado contra la frase de
/// <c>CLAUDE.md</c> §2 que lo afirma.
/// </summary>
/// <remarks>
/// <para><b>El defecto que evita.</b> Esa frase decía «UNA» durante once HU seguidas —#24, #25,
/// #33a, #35, #36, #40, #44, #45, #46, #62 y #15— y un agente que la leyera concluía que no había
/// nada cableado y proponía de cero lo que ya existía. Se corrigió a mano a «siete», y para
/// cuando se escribió este gate ya volvía a estar mal: eran <b>nueve</b>. Le faltaban
/// <c>Api.Cart</c> —séptima rebanada de la HU #14— y <c>Api.Payments</c> —el cobro de la tasa de
/// un trámite, #27—, las dos entradas después de la última vez que alguien contó.</para>
///
/// <para><b>Por qué se desvía SIEMPRE, y por qué un número a mano no lo arregla.</b> Cablear una
/// capacidad nueva es trabajo de una HU concreta, y esa HU toca su seam, su composer y su
/// interruptor; volver a §2 a subir un contador es un paso que no se le ocurre a nadie porque no
/// forma parte del trabajo. Es la misma forma de «la compensación cruzada (48)», que fue exacta
/// el día que se escribió y siguió ahí siete ficheros después.</para>
///
/// <para><b>Cobertura, no cuenta</b> — la lección del Caddyfile (#114) aplicada acá. No se
/// comprueba un número: se comprueba que la frase <b>nombre</b> exactamente las capacidades que
/// el código alcanza. Un gate que sólo mirase la cifra se conformaría con «nueve» y una lista
/// equivocada, que es peor que una cifra equivocada: la lista es lo que alguien lee para saber
/// qué ya existe.</para>
///
/// <para><b>Cómo se deriva, y por qué por RUTA y no por nombre.</b> Un cliente HTTP del CMS no
/// nombra a la capacidad con la que habla —a propósito: su URL llega por configuración, que es lo
/// que permite moverla—. Lo único que la identifica es la <b>ruta</b> que pide. Así que se cruzan
/// las dos mitades: qué rutas <c>/v1/</c> declara cada <c>Synergos.Api.*</c> y
/// <c>Synergos.Bff.*</c> en sus <c>Endpoints/</c>, y qué rutas piden los clientes del CMS. Es el
/// mismo movimiento que el gate del proxy: medir la cobertura real en vez de contar entradas de
/// una lista.</para>
///
/// <para><b>Y se exige que el cliente esté CABLEADO, no que exista.</b> Una clase
/// <c>Http*Service</c> que ningún composer registra no es una conexión: es código que nadie
/// ejecuta. Sin esta mitad, el gate contaría como «conectada» una capacidad a la que el producto
/// no le habla — que es exactamente el fallo que este repo ya nombró midiendo si el verificador
/// de webhooks estaba <i>enchufado</i> y no si estaba <i>escrito</i>.</para>
///
/// <para><b>Una ruta que declaran DOS capacidades no cuenta para ninguna</b>, y hace falta:
/// <c>/v1/holds</c> lo declaran <c>Api.Booking</c> y <c>Api.Inventory</c>, así que resolver por
/// esa ruta sola daría <c>Api.Inventory</c> por conectada cuando nadie le habla. Una capacidad se
/// da por alcanzada sólo si algún cliente cableado pide una ruta que <b>únicamente ella</b>
/// declara — <c>Api.Booking</c> entra por <c>/v1/resources</c>, y <c>Api.Inventory</c> no entra.
/// Es conservador a propósito: prefiere quedarse corto y decirlo a inventar una conexión.</para>
///
/// <para><b>Se parsea sobre la fuente SIN comentarios</b>
/// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>): las cabeceras de estos
/// ficheros citan rutas de otras capacidades para explicar por qué NO se les llama, y contarlas
/// haría que el gate leyera su propia documentación como si fuera cableado.</para>
/// </remarks>
public sealed class CapacidadesConectadasTests
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

    /// <summary>El fichero sin comentarios ni prosa de XML doc.</summary>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta)
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal)
                     && !l.StartsWith('*')
                     && !l.StartsWith("/*", StringComparison.Ordinal)));

    /// <summary>El primer segmento de una ruta <c>/v1/algo/...</c> — lo que identifica al dueño.</summary>
    /// <remarks>
    /// Se corta en el primer segmento porque es lo único que los dos lados escriben igual: el
    /// borde declara <c>/v1/instances/{id}/fire</c> y el cliente pide
    /// <c>$"v1/instances/{x.Id}/fire"</c>, con el interpolado en medio. El segundo segmento de un
    /// cliente casi nunca es literal.
    /// </remarks>
    private static string? Familia(string ruta)
    {
        var limpia = ruta.TrimStart('/');
        if (!limpia.StartsWith("v1/", StringComparison.Ordinal)) return null;

        var resto = limpia[3..].Split('?')[0].Split('/')[0].Trim();
        return resto.Length == 0 || resto.StartsWith('{') ? null : $"v1/{resto}";
    }

    /// <summary>Qué familias de ruta declara cada servicio, leídas de sus <c>Endpoints/</c>.</summary>
    private static Dictionary<string, HashSet<string>> FamiliasPorServicio()
    {
        var raiz = RepoRoot();
        var mapa = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var declara = new Regex(@"\.Map(?:Get|Post|Delete|Put|Patch|Methods)\(\s*""(/v1/[^""]*)""", RegexOptions.Compiled);

        foreach (var proyecto in Proyectos.Todos("Synergos.Api.")
                     .Concat(Proyectos.Todos("Synergos.Bff."))
                     .Where(d => !d.EndsWith(".Core", StringComparison.Ordinal)))
        {
            var familias = new HashSet<string>(StringComparer.Ordinal);

            foreach (var cs in Directory.EnumerateFiles(proyecto, "*.cs", SearchOption.AllDirectories)
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                  && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                foreach (Match m in declara.Matches(SinComentarios(cs)))
                {
                    if (Familia(m.Groups[1].Value) is { } f) familias.Add(f);
                }
            }

            if (familias.Count > 0) mapa[Path.GetFileName(proyecto)] = familias;
        }

        return mapa;
    }

    /// <summary>Los clientes HTTP del CMS que algún composer registra, con las rutas que piden.</summary>
    /// <remarks>
    /// «Registrado» se mide buscando el nombre de la clase en <c>Composers/</c>. Es deliberadamente
    /// laxo —no distingue qué interruptor lo enciende— porque lo que esta frase afirma es que el
    /// cableado EXISTE, no que esté encendido: §2 dice, en la misma línea, que los interruptores
    /// están apagados por defecto.
    /// </remarks>
    private static Dictionary<string, HashSet<string>> FamiliasPorClienteCableado()
    {
        var raiz = RepoRoot();
        var composers = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(raiz, "Synergos.CMS.Web", "Composers"), "*.cs", SearchOption.AllDirectories)
            .Select(SinComentarios));

        var pide = new Regex(@"""(v1/[^""]*)""", RegexOptions.Compiled);
        var mapa = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var cs in Directory.EnumerateFiles(
                     Path.Combine(raiz, "Synergos.CMS.Web", "Services"), "Http*.cs"))
        {
            var clase = Path.GetFileNameWithoutExtension(cs);
            if (!composers.Contains(clase, StringComparison.Ordinal)) continue;

            var familias = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in pide.Matches(SinComentarios(cs)))
            {
                if (Familia(m.Groups[1].Value) is { } f) familias.Add(f);
            }

            if (familias.Count > 0) mapa[clase] = familias;
        }

        return mapa;
    }

    /// <summary>Los servicios que el CMS alcanza de verdad, por ruta EXCLUSIVA.</summary>
    private static SortedSet<string> Alcanzados()
    {
        var declaradas = FamiliasPorServicio();

        // Una familia que declaran dos servicios no identifica a ninguno.
        var exclusivas = declaradas
            .SelectMany(kv => kv.Value.Select(f => (Familia: f, Servicio: kv.Key)))
            .GroupBy(x => x.Familia, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().Servicio, StringComparer.Ordinal);

        var alcanzados = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var familia in FamiliasPorClienteCableado().SelectMany(kv => kv.Value))
        {
            if (exclusivas.TryGetValue(familia, out var servicio)) alcanzados.Add(servicio);
        }

        return alcanzados;
    }

    /// <summary>La guía en una sola línea, sin los <c>&gt;</c> de los blockquotes.</summary>
    /// <remarks>
    /// Quitarlos hace falta: la frase que este gate cruza vive dentro de un blockquote y el salto
    /// de línea le mete un <c>&gt;</c> EN MEDIO —«con **siete** &gt; capacidades»—, así que
    /// colapsar espacios a secas deja una frase que no se puede buscar. Sin esto, arreglar la
    /// guía haría fallar el gate según por dónde parta la línea, que es la clase de gate que
    /// alguien acaba borrando.
    /// </remarks>
    private static string Normalizada(string markdown)
        => Regex.Replace(
            string.Join('\n', markdown.Split('\n').Select(l => Regex.Replace(l, @"^\s*>\s?", ""))),
            @"\s+", " ");

    /// <summary>La frase de la guía que arranca en <paramref name="desde"/>, hasta el punto final.</summary>
    /// <remarks>
    /// Acota el cruce de nombres a la frase que los afirma. Buscarlos en el fichero entero es lo
    /// que hacía pasar este gate en verde con dos capacidades sin nombrar: `Cart` y `Payments`
    /// aparecen en la tabla de actores de §11, que habla de quién firma un asiento y no de con
    /// quién habla el CMS.
    /// </remarks>
    private static string Frase(string guia, string desde)
    {
        var i = guia.IndexOf(desde, StringComparison.Ordinal);
        Assert.True(i >= 0,
            $"CLAUDE.md §2 ya no dice «{desde}». Si se reescribió la frase, hay que mover este "
            + "gate con ella — no borrarlo: es la frase que dijo «UNA» durante once HU.");

        var fin = guia.IndexOf(". ", i, StringComparison.Ordinal);
        return fin < 0 ? guia[i..] : guia[i..(fin + 1)];
    }

    private static readonly string[] Numerales =
    {
        "cero", "una", "dos", "tres", "cuatro", "cinco", "seis", "siete", "ocho", "nueve", "diez",
        "once", "doce", "trece", "catorce", "quince", "dieciséis", "diecisiete", "dieciocho",
        "diecinueve", "veinte",
    };

    private static string Numeral(int n)
        => n >= 0 && n < Numerales.Length ? Numerales[n] : n.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// §2 nombra EXACTAMENTE las capacidades que el CMS alcanza, y dice cuántas son.
    /// </summary>
    [Fact]
    public void La_frase_de_la_seccion_2_nombra_las_capacidades_que_el_codigo_alcanza()
    {
        var alcanzados = Alcanzados();
        var capacidades = alcanzados.Where(s => s.StartsWith("Synergos.Api.", StringComparison.Ordinal))
            .Select(s => s["Synergos.Api.".Length..]).ToList();

        // Red de seguridad: un descubrimiento roto dejaría el gate comparando contra el vacío, y
        // el build se arreglaría escribiendo «cero» en la guía.
        Assert.True(capacidades.Count >= 5,
            $"Sólo se derivaron {capacidades.Count} capacidades alcanzadas ({string.Join(", ", capacidades)}). "
            + "El descubrimiento está roto: se esperan las rutas de Synergos.CMS.Web/Services/Http*.cs "
            + "cruzadas contra los Endpoints/ de cada Synergos.Api.*.");

        var guia = Normalizada(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")));
        var cifra = $"El CMS habla hoy con **{Numeral(capacidades.Count)}** capacidades";
        var problemas = new List<string>();

        if (!guia.Contains(cifra, StringComparison.Ordinal))
        {
            problemas.Add($"CLAUDE.md §2 no dice «{cifra}» — el código alcanza {capacidades.Count}: "
                + string.Join(", ", capacidades));
        }

        // Los nombres se buscan DENTRO de la frase, no en el fichero entero, y la diferencia no
        // es cosmética: la primera versión de este gate buscaba en todo `CLAUDE.md` y daba por
        // nombradas a `Cart` y `Payments` — que salen en la tabla de actores de §11, hablando de
        // otra cosa. O sea que pasaba en verde con la lista de §2 incompleta, que es justo el
        // defecto que viene a cazar.
        var frase = Frase(guia, "El CMS habla hoy con");

        var faltan = capacidades.Where(c => !frase.Contains($"`{c}`", StringComparison.Ordinal)).ToList();

        if (faltan.Count > 0)
        {
            problemas.Add("La frase de §2 no NOMBRA estas capacidades, y el CMS les habla: "
                + string.Join(", ", faltan.Select(c => $"`{c}`"))
                + Environment.NewLine + "   frase leída: " + frase);
        }

        Assert.True(problemas.Count == 0,
            string.Join(Environment.NewLine, problemas)
            + Environment.NewLine
            + "Esta frase se deriva, no se recuerda: dijo «UNA» durante once HU y «siete» cuando ya "
            + "eran nueve. La lista importa más que la cifra — es lo que alguien lee para saber qué "
            + "ya existe antes de proponerlo de cero.");
    }

    /// <summary>
    /// Y la mitad de los orquestadores de la misma frase, por el mismo camino.
    /// </summary>
    /// <remarks>
    /// Va aparte porque se desvía por su cuenta: §11 ya arrastró un «los dos orquestadores»
    /// después de que <c>Bff.Eventos</c> y <c>Bff.Viajes</c> heredaran los barridos de
    /// <c>AddSagaMachinery</c> sin que nadie tocara una línea.
    /// </remarks>
    [Fact]
    public void La_frase_de_la_seccion_2_cuenta_bien_los_orquestadores()
    {
        var bffs = Alcanzados().Count(s => s.StartsWith("Synergos.Bff.", StringComparison.Ordinal));

        Assert.True(bffs > 0, "No se derivó ningún orquestador alcanzado: el descubrimiento está roto.");

        var guia = Normalizada(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")));
        var frase = $"y con los {Numeral(bffs)} orquestadores";

        Assert.True(guia.Contains(frase, StringComparison.Ordinal),
            $"CLAUDE.md §2 no dice «{frase}» — el CMS alcanza {bffs} orquestadores.");
    }
}
