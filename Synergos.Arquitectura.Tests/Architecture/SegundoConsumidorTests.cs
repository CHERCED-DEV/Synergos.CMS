using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Cuántos consumidores tiene cada capacidad, <b>derivado del disco</b> y cruzado contra la
/// lista que <c>CLAUDE.md</c> §11 nombra (#169).
/// </summary>
/// <remarks>
/// <para><b>Qué pregunta contesta, y por qué no la contestaba nada.</b> §0.B.17 dice que
/// <i>se promueve al SEGUNDO consumidor, no antes</i>, y ese criterio se aplicó a
/// <c>Synergos.Shared</c> —esperó a seis— y a <c>Synergos.Bff.Core</c> —esperó a dos—. Nunca se
/// aplicó a las <b>capacidades mismas</b>. Que tengan la FORMA de una pieza reusable —cero
/// sustantivos de negocio, cero ramas sobre <c>Ref.Kind</c>, las dos con gate— se comprueba
/// leyendo; que alguien las haya <b>vuelto a usar</b> sólo se comprueba contando. Medido al
/// escribir esto: <b>5 con dos o más, 10 con uno, 5 con ninguno</b>.</para>
///
/// <para><b>Esto NO es un trinquete, y la distinción decide el diseño.</b> Una capacidad sin
/// segundo consumidor no es un defecto: las veinte se construyeron <i>a propósito</i> antes que
/// sus consumidores (§11 lo documenta). Un umbral que se pusiera rojo al nacer la capacidad 21
/// con un consumidor marcaría el <b>caso normal</b>, y un gate que marca al bueno enseña a
/// ignorarlo (#158). Lo que se vigila es que la <b>guía no envejezca</b>: que §11 nombre
/// exactamente lo que el disco dice. Es el mismo trato que
/// <see cref="CapacidadesConectadasTests"/>.</para>
///
/// <para><b>Y es una LISTA, no una cifra</b>
/// (<c>feedback_a_named_list_beats_a_count</c>): un gate que cuadrara «cinco» se conforma con
/// cinco nombres equivocados, y la lista es lo que alguien lee para decidir qué retirar, qué
/// cablear y qué dejar. La frase de §11 que esto cruza ya costó once HU diciendo «UNA
/// capacidad».</para>
///
/// <para><b>Cómo se atribuye, y son DOS criterios porque los dos lados escriben cosas
/// distintas.</b> No es la misma regla medida dos veces —eso sería
/// <c>feedback_the_same_algorithm_is_not_the_same_thing</c>— sino la mejor evidencia que hay a
/// cada lado:</para>
/// <list type="bullet">
///   <item><b>Un <c>Synergos.Bff.*</c> NOMBRA a la capacidad</b>
///     (<c>public const string Inventory = "inventory";</c>, usada en
///     <c>Get&lt;T&gt;(Inventory, …)</c>), así que la atribución es exacta y barata.</item>
///   <item><b>Un cliente del CMS NO la nombra</b> —a propósito: su URL llega por configuración,
///     que es lo que permite moverla— así que lo único que la identifica es la <b>ruta</b>
///     exclusiva que pide, que es el criterio de <see cref="CapacidadesConectadasTests"/>.</item>
/// </list>
///
/// <para><b>Los dos cortes que costaron su medición, y los dos acusaban a un inocente.</b> La
/// primera versión atribuía TODO por ruta exclusiva, como el gate hermano, y dio <b>4/9/7</b>:</para>
/// <list type="number">
///   <item><b><c>Api.Inventory</c> salía con CERO consumidores</b> teniendo dos flujos encima.
///     Todas sus familias de ruta están compartidas —<c>v1/items</c> con <c>Api.Catalog</c> y
///     <c>v1/holds</c> con <c>Api.Booking</c>— así que la regla conservadora del gate hermano no
///     puede atribuir ninguna. Para «¿está conectada?» quedarse corto es correcto y está dicho;
///     para «¿cuántos consumidores tiene?» un cero falso manda a alguien a <b>retirar</b> una
///     capacidad de la que cuelgan Tienda y Eventos. El nombre lo arregla.</item>
///   <item><b><c>Api.Notifications</c> perdía su único consumidor</b>, que es
///     <c>Bff.Core.DeliverySweeper</c>. <c>Bff.Core</c> se excluye bien como <i>declarante</i>
///     —no publica endpoints— y se excluía mal como <i>consumidor</i>. Un orquestador compartido
///     consume igual que uno vertical.</item>
/// </list>
///
/// <para>Con los dos cortes, la derivación da <b>5/10/5</b> y <b>coincide con el conteo hecho a
/// mano</b> en el hallazgo. Dos derivaciones independientes de la misma verdad que tienen que
/// coincidir es lo único que distingue «lo medí» de «creo que lo medí» — el movimiento de
/// <c>IdentityGateTests</c>.</para>
///
/// <para><b>Se parsea sobre la fuente SIN comentarios, y la dirección se MIDIÓ.</b> Escribí
/// primero que era «(c), falso negativo» y la mutación lo desmintió: apagando el barrido, los
/// tres tests siguen en <b>verde</b>. Es <b>(a), no-op hoy</b> — hoy ningún comentario del árbol
/// cita una constante en posición de llamada ni una ruta que cambie la atribución. Se conserva
/// porque el disco crece hacia el caso: las cabeceras de esta zona citan capacidades a las que
/// <i>no</i> se llama, para explicar por qué —<c>PropagacionDeIdentidadTests</c> vive de eso—, y
/// la primera que lo haga en la forma que el corte reconoce sumaría un consumidor que no
/// existe.</para>
///
/// <para>Decirlo así en vez de dejar la afirmación bonita es el addendum de
/// <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>: <b>la dirección se mide, no
/// se supone</b>, y una explicación que suena bien es exactamente donde nadie vuelve a
/// mirar.</para>
/// </remarks>
public sealed class SegundoConsumidorTests
{
    /// <summary>Piso de descubrimiento: eran veinte al escribir esto.</summary>
    private const int MinimoDeCapacidades = 20;

    /// <summary>
    /// Piso de atribución. Sin él, un recorrido roto deja todas las capacidades con cero
    /// consumidores y el gate se limita a exigir que la guía diga eso — verde sobre el vacío,
    /// que es el 12/12 del #136.
    /// </summary>
    private const int MinimoDeVinculos = 15;

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

    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta)
            .Select(l => l.TrimStart())
            .Where(l => !l.StartsWith("//", StringComparison.Ordinal)
                     && !l.StartsWith('*')
                     && !l.StartsWith("/*", StringComparison.Ordinal)));

    private static IEnumerable<string> Fuentes(string proyecto)
        => Directory.EnumerateFiles(proyecto, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>El primer segmento de una ruta <c>/v1/algo/...</c>.</summary>
    private static string? Familia(string ruta)
    {
        var limpia = ruta.TrimStart('/');
        if (!limpia.StartsWith("v1/", StringComparison.Ordinal)) return null;

        var resto = limpia[3..].Split('?')[0].Split('/')[0].Trim();
        return resto.Length == 0 || resto.StartsWith('{') ? null : $"v1/{resto}";
    }

    /// <summary>El nombre corto con el que un orquestador nombra a la capacidad.</summary>
    /// <remarks>
    /// <c>Synergos.Api.Inventory</c> ↔ <c>"inventory"</c>. Es una convención del repo y por eso
    /// se deriva en vez de escribirse: una tabla a mano de veinte filas se desvía a la primera
    /// capacidad nueva, que es el defecto que este gate existe para cerrar un piso más arriba.
    /// </remarks>
    private static string Corto(string capacidad)
        => capacidad.Split('.')[^1].ToLowerInvariant();

    /// <summary>Quién consume cada capacidad.</summary>
    private static SortedDictionary<string, SortedSet<string>> Consumidores(out int vinculos)
    {
        var capacidades = Proyectos.Todos("Synergos.Api.")
            .Select(Path.GetFileName)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

        var mapa = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);
        foreach (var c in capacidades) mapa[c] = new SortedSet<string>(StringComparer.Ordinal);

        // ── Los orquestadores: la capacidad viene NOMBRADA ───────────────────────────────
        //
        // Se cuenta el USO de la constante y no su declaración: una constante declarada y no
        // usada es vocabulario, no una llamada. `CreateClient(Capability)` va sin coma detrás,
        // así que el corte acepta las dos formas.
        var declara = new Regex(@"const\s+string\s+(\w+)\s*=\s*""([a-z0-9-]+)""", RegexOptions.Compiled);

        foreach (var proyecto in Proyectos.Todos("Synergos.Bff."))
        {
            var orquestador = Path.GetFileName(proyecto)!;
            var texto = string.Join('\n', Fuentes(proyecto).Select(SinComentarios));

            foreach (Match m in declara.Matches(texto))
            {
                var dueño = capacidades.FirstOrDefault(
                    c => string.Equals(Corto(c), m.Groups[2].Value, StringComparison.Ordinal));
                if (dueño is null) continue;

                var usada = new Regex(@"[<>\w]\(\s*" + Regex.Escape(m.Groups[1].Value) + @"\s*[,)]");
                if (usada.IsMatch(texto)) mapa[dueño].Add(orquestador);
            }
        }

        // ── El CMS: la capacidad NO viene nombrada, así que se resuelve por ruta EXCLUSIVA ──
        var mapPost = new Regex(@"\.Map(?:Get|Post|Delete|Put|Patch|Methods)\(\s*""(/v1/[^""]*)""", RegexOptions.Compiled);
        var porFamilia = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var proyecto in Proyectos.Todos("Synergos.Api.").Concat(Proyectos.Todos("Synergos.Bff."))
                     .Where(d => !Path.GetFileName(d)!.EndsWith(".Core", StringComparison.Ordinal)))
        {
            var servicio = Path.GetFileName(proyecto)!;
            foreach (var cs in Fuentes(proyecto))
            {
                foreach (Match m in mapPost.Matches(SinComentarios(cs)))
                {
                    if (Familia(m.Groups[1].Value) is not { } f) continue;
                    if (!porFamilia.TryGetValue(f, out var duenos)) porFamilia[f] = duenos = new HashSet<string>(StringComparer.Ordinal);
                    duenos.Add(servicio);
                }
            }
        }

        var exclusivas = porFamilia
            .Where(kv => kv.Value.Count == 1)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Single(), StringComparer.Ordinal);

        var raiz = RepoRoot();
        var composers = string.Join('\n', Directory
            .EnumerateFiles(Path.Combine(raiz, "Synergos.CMS.Web", "Composers"), "*.cs", SearchOption.AllDirectories)
            .Select(SinComentarios));

        var pide = new Regex(@"""(v1/[^""]*)""", RegexOptions.Compiled);
        foreach (var cs in Directory.EnumerateFiles(Path.Combine(raiz, "Synergos.CMS.Web", "Services"), "Http*.cs"))
        {
            var clase = Path.GetFileNameWithoutExtension(cs);
            if (!composers.Contains(clase, StringComparison.Ordinal)) continue;

            foreach (Match m in pide.Matches(SinComentarios(cs)))
            {
                if (Familia(m.Groups[1].Value) is not { } f) continue;
                if (exclusivas.TryGetValue(f, out var servicio) && mapa.TryGetValue(servicio, out var quienes))
                {
                    quienes.Add("CMS");
                }
            }
        }

        vinculos = mapa.Sum(kv => kv.Value.Count);
        return mapa;
    }

    /// <summary>La guía en una línea, sin los <c>&gt;</c> de los blockquotes.</summary>
    private static string Normalizada(string markdown)
        => Regex.Replace(
            string.Join('\n', markdown.Split('\n').Select(l => Regex.Replace(l, @"^\s*>\s?", ""))),
            @"\s+", " ");

    private static string Guia() => Normalizada(File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md")));

    [Fact]
    public void El_descubrimiento_ve_las_capacidades_y_sus_consumidores()
    {
        var mapa = Consumidores(out var vinculos);

        Assert.True(
            mapa.Count >= MinimoDeCapacidades,
            $"sólo se descubrieron {mapa.Count} capacidades y eran {MinimoDeCapacidades}. Si el "
            + "recorrido dejó de verlas, los dos dientes de abajo pasarían en verde sobre una "
            + "lista vacía (#136).");

        Assert.True(
            vinculos >= MinimoDeVinculos,
            $"sólo se atribuyeron {vinculos} vínculos capacidad↔consumidor y eran al menos "
            + $"{MinimoDeVinculos}. Con el recorrido roto todas saldrían con cero consumidores y "
            + "el gate se limitaría a exigir que la guía dijera eso.");
    }

    [Fact]
    public void CLAUDE_md_nombra_las_capacidades_SIN_un_segundo_consumidor()
    {
        var mapa = Consumidores(out _);
        var guia = Guia();

        var sinNinguno = mapa.Where(kv => kv.Value.Count == 0).Select(kv => Corto(kv.Key)).ToList();
        var faltan = sinNinguno.Where(c => !guia.Contains($"`Api.{Capitalizar(c)}`", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.True(
            faltan.Count == 0,
            "CLAUDE.md no nombra estas capacidades, que el disco dice que NO tienen ni un "
            + $"consumidor: {string.Join(", ", faltan)}. La lista es lo que alguien lee para "
            + "decidir qué retirar y qué cablear, así que una cifra correcta con la lista "
            + "incompleta es peor que una cifra equivocada (#169).");
    }

    /// <summary>
    /// La frase de §11 que afirma el reparto, cruzada contra el disco en los DOS sentidos.
    /// </summary>
    /// <remarks>
    /// Los dos sentidos hacen falta por lo de siempre: sin el segundo, una capacidad que
    /// estrena su segundo consumidor deja la guía diciendo que todavía no lo tiene, y el commit
    /// que lo cableó no se entera. Es lo que pasó con «el CMS habla con UNA capacidad» durante
    /// once HU.
    /// </remarks>
    [Fact]
    public void La_frase_de_la_guia_nombra_exactamente_las_reutilizadas()
    {
        var mapa = Consumidores(out _);
        var guia = Guia();

        const string marca = "Con SEGUNDO consumidor (o sea reutilización PROBADA):";
        var i = guia.IndexOf(marca, StringComparison.Ordinal);
        Assert.True(i >= 0, $"CLAUDE.md §11 tiene que llevar la frase «{marca}» — es lo que este gate cruza.");

        // El corte es por PUNTO SEGUIDO DE ESPACIO y no por punto a secas: los nombres que la
        // frase enumera llevan punto dentro (`Api.Booking`), así que cortar en el primero deja
        // la frase en «… PROBADA): `Api.» y el gate acusa a las cinco de no estar nombradas.
        // Lo destapó correrlo, no leerlo — que es la misma lección que el `| jq length` del CDN.
        var fin = guia.IndexOf(". ", i + marca.Length, StringComparison.Ordinal);
        var frase = guia[i..(fin > 0 ? fin + 1 : guia.Length)];

        var reutilizadas = mapa.Where(kv => kv.Value.Count >= 2).Select(kv => Corto(kv.Key)).ToList();

        var noNombradas = reutilizadas
            .Where(c => !frase.Contains($"`Api.{Capitalizar(c)}`", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.True(
            noNombradas.Count == 0,
            $"el disco dice que estas capacidades YA tienen un segundo consumidor y la frase de "
            + $"§11 no las nombra: {string.Join(", ", noNombradas)}. Frase medida: «{frase}».");

        var deMas = Regex.Matches(frase, @"`Api\.(\w+)`")
            .Select(m => m.Groups[1].Value.ToLowerInvariant())
            .Where(c => !reutilizadas.Contains(c, StringComparer.Ordinal))
            .ToList();
        Assert.True(
            deMas.Count == 0,
            $"la frase de §11 nombra estas capacidades como reutilizadas y el disco dice que "
            + $"todavía no lo están: {string.Join(", ", deMas)}. Bajala en el mismo commit — una "
            + "guía que afirma de más es peor que una que se queda corta, porque el siguiente "
            + "agente construye encima.");
    }

    private static string Capitalizar(string corto)
        => corto.Length == 0 ? corto : char.ToUpperInvariant(corto[0]) + corto[1..];
}
