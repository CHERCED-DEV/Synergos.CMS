using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El molde de un VERTICAL, como invariante ejecutable (HU #115).
/// </summary>
/// <remarks>
/// <para><b>El molde de una CAPACIDAD ya estaba escrito y gateado</b> —doc 08 §4 y
/// <c>ApiMoldTests</c>—, así que un agente nuevo puede escribir la capacidad veintiuno sin
/// preguntarle a nadie. El molde de un vertical no estaba, y el vertical es la unidad en la que
/// de verdad se agrega producto: añadir el octavo obligaba a leerse los siete que hay y adivinar
/// qué partes eran esenciales y cuáles fueron accidentes de quien las escribió. Este gate es la
/// mitad ejecutable de <c>docs/product/12-el-molde-de-un-vertical.md</c>.</para>
///
/// <para><b>El descubrimiento se DERIVA, no se enumera</b>, y el criterio elegido es el
/// interruptor: todo <c>Config["Synergos:…:Mode"]</c> que un composer lee para cambiar una
/// implementación en proceso por un cliente <c>Http*</c> del otro árbol. Se eligió ése y no los
/// <c>*WiringTests</c> —que también se podían recorrer— porque un gate que se descubre a sí mismo
/// por el nombre de sus ficheros deja invisible justo el fallo que más importa: el vertical que se
/// cableó sin gate. Aquí ese caso sale ROJO (ver <see cref="Cada_punto_de_cableado_tiene_un_gate_que_nombra_su_cliente"/>).</para>
///
/// <para><b>Y por eso el primer test afirma que el descubrimiento VE</b>, como hace
/// <c>WebhookGateTests</c>: un gate cuya lista sale vacía pasa en verde y no vigila nada, que es
/// peor que no tenerlo porque da la señal de que se está vigilando.</para>
///
/// <para><b>Lo que este gate no puede contestar, y el doc sí:</b> si un vertical necesita
/// orquestador o no. Eso lo deciden las tres preguntas del repo —¿hay algo que <b>deshacer</b>?,
/// ¿el recurso lo lleva <b>alguien más</b>?, ¿<b>quién tiene la plata</b>?— y ninguna se lee del
/// disco. Lo que sí se puede vigilar es la <b>consecuencia</b> de haberlas contestado mal, y es
/// lo que hace <see cref="En_modo_Api_un_interruptor_gobierna_UNA_capacidad"/>: el error que
/// <c>CLAUDE.md</c> §11 documenta CUATRO veces seguidas es mirar «cuántos pasos compone» en vez
/// de las tres preguntas, y su huella en el árbol es siempre la misma — dos capacidades colgando
/// de un solo interruptor directo.</para>
/// </remarks>
public sealed class MoldeDelVerticalTests
{
    // ── El modelo ───────────────────────────────────────────────────────────

    /// <summary>Un punto de cableado: un interruptor y todo lo que cuelga de él.</summary>
    private sealed record Punto(
        string Clave,
        string Seccion,
        string Vertical,
        string ModoCableado,
        string Composer,
        string Rama,
        string? Poco,
        IReadOnlyList<string> CamposDelPoco,
        string? DefaultDelPoco,
        IReadOnlyList<string> Clientes);

    /// <summary>Las dos palabras del molde. Todo lo demás es anterior a él.</summary>
    private static readonly string[] Vocabulario = ["Api", "Bff"];

    /// <summary>
    /// Cuántos puntos hablan con el árbol de servicios con un vocabulario que no es el del molde.
    /// </summary>
    /// <remarks>
    /// <b>Es un trinquete, no una lista de excepciones</b> — la misma forma que
    /// <c>tools/contract-keys.baseline.json</c>. Hoy son dos y los dos son anteriores al molde:
    /// <c>Synergos:SearchAnalytics:Mode</c> (<c>Sessions</c>, ADR 0130 — el consumidor más viejo
    /// del árbol, escrito antes de que hubiera molde) y <c>Synergos:BundleRegistry:Mode</c>
    /// (<c>Http</c>, ADR 0132 — el CDN, que es público y por eso no lleva llave compartida ni
    /// puede tener los cuatro campos). No se arreglan aquí porque cambiarlos cambia
    /// comportamiento y eso es otro ticket; lo que este número impide es que la deuda CREZCA.
    /// </remarks>
    private const int PuntosAnterioresAlMolde = 2;

    /// <summary>Los verticales que sabemos que existen. Solo para afirmar que el gate VE.</summary>
    private static readonly string[] VerticalesConocidos =
        ["Tienda", "Salud", "Realty", "Gob", "Eventos", "Viajes", "Academy"];

    // ── Lectura del disco ───────────────────────────────────────────────────

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

    private static string Dir(params string[] partes) => Path.Combine([RepoRoot(), .. partes]);

    /// <summary>El fichero SIN comentarios.</summary>
    /// <remarks>
    /// <b>Hace falta y ya lo demostró una mutación ajena</b> (<c>RealtyWiringTests</c>): estos
    /// composers explican en prosa lo que hacen, así que un gate que busque
    /// <c>HttpVisitSchedulingService</c> sobre el texto crudo lo encuentra en el comentario aunque
    /// el registro haya desaparecido. Se leería la explicación, no la implementación.
    /// </remarks>
    private static string SinComentarios(string ruta)
        => string.Join('\n', File.ReadAllLines(ruta).Select(l =>
        {
            var t = l.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith("*", StringComparison.Ordinal)
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                return string.Empty;
            }
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? l[..i] : l;
        }));

    private static IReadOnlyList<(string Nombre, string Codigo)> Composers()
        => Directory.EnumerateFiles(Dir("Synergos.CMS.Web", "Composers"), "*.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), SinComentarios(f)))
            .ToList();

    private static IReadOnlyList<string> ClientesHttp()
        => Directory.EnumerateFiles(Dir("Synergos.CMS.Web", "Services"), "Http*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>El índice de la llave que cierra la que abre en <paramref name="apertura"/>.</summary>
    private static int Cierre(string s, int apertura)
    {
        var d = 0;
        for (var k = apertura; k < s.Length; k++)
        {
            if (s[k] == '{') d++;
            else if (s[k] == '}' && --d == 0) return k;
        }
        return s.Length - 1;
    }

    /// <summary>El cierre del método que contiene <paramref name="i"/>.</summary>
    private static int FinDelMetodo(string s, int i)
    {
        var d = 0;
        for (var k = i; k >= 0; k--)
        {
            if (s[k] == '}') d++;
            else if (s[k] == '{')
            {
                if (d == 0) return Cierre(s, k);
                d--;
            }
        }
        return s.Length;
    }

    /// <summary>
    /// El texto de la rama cableada: el bloque del <c>if</c> si lo tiene, y si es una guarda de
    /// salida temprana (<c>if (!EsModoApi(…)) return;</c>) lo que queda del método.
    /// </summary>
    /// <remarks>
    /// Las dos formas están en el árbol —<c>SeamComposer.Shop.cs</c> usa bloque,
    /// <c>SeamComposer.Tracking.cs</c> usa guarda— y un gate que solo entendiera la primera
    /// dejaría al seguimiento de pedidos sin vigilar sin que nadie se enterara.
    /// </remarks>
    private static string Rama(string src, int desdeElIf)
    {
        var parentesis = 0;
        for (var i = desdeElIf; i < src.Length; i++)
        {
            var c = src[i];
            if (c == '(') parentesis++;
            else if (c == ')') parentesis--;
            else if (parentesis == 0 && c == ';') return src[desdeElIf..FinDelMetodo(src, desdeElIf)];
            else if (parentesis == 0 && c == '{') return src[desdeElIf..(Cierre(src, i) + 1)];
        }
        return src[desdeElIf..];
    }

    /// <summary>El cuerpo de un método del mismo fichero, para seguir al ayudante que llama.</summary>
    /// <remarks>
    /// Sin esto, Tienda saldría sin llave compartida y sin correlación: las pone
    /// <c>ConfigurarClienteTienda</c>, que la rama invoca dos veces. Un gate que solo mirara la
    /// rama concluiría que el cliente sale a la red desnudo.
    /// </remarks>
    private static string Cuerpo(string src, string metodo)
    {
        var m = Regex.Match(src, @"\b(?:static|private|public|internal)[^;{]{0,140}?\b" + Regex.Escape(metodo) + @"\s*\(");
        if (!m.Success) return string.Empty;
        var llave = src.IndexOf('{', m.Index);
        return llave < 0 ? string.Empty : src[m.Index..(Cierre(src, llave) + 1)];
    }

    // ── El descubrimiento ───────────────────────────────────────────────────

    /// <summary>
    /// Todo interruptor <c>Synergos:…:Mode</c> que cambia una implementación en proceso por un
    /// cliente <c>Http*</c> hacia el otro árbol.
    /// </summary>
    private static IReadOnlyList<Punto> Puntos()
    {
        var composers = Composers();
        var clientes = ClientesHttp();
        var pocos = Directory.EnumerateFiles(Dir("Synergos.CMS.Application", "Configuration"), "*Settings.cs")
            .ToDictionary(Path.GetFileNameWithoutExtension!, f => File.ReadAllText(f), StringComparer.Ordinal);

        var encontrados = new List<Punto>();

        foreach (var (nombre, src) in composers)
        {
            var ifs = Regex.Matches(src, @"\bif\s*\(").Select(m => m.Index).ToList();

            foreach (Match marca in Regex.Matches(src, @"Config\[""(Synergos:[A-Za-z:]+:Mode)""\]"))
            {
                var clave = marca.Groups[1].Value;
                if (encontrados.Any(p => p.Clave == clave)) continue;

                var previo = ifs.LastOrDefault(i => i < marca.Index, -1);
                var inicio = previo >= 0 && marca.Index - previo < 300 ? previo : marca.Index;

                var texto = Rama(src, inicio);
                foreach (var ayudante in Regex.Matches(texto, @"\b([A-Z][A-Za-z0-9]{3,})\s*\(")
                             .Select(m => m.Groups[1].Value).Distinct(StringComparer.Ordinal))
                {
                    var cuerpo = Cuerpo(src, ayudante);
                    if (cuerpo.Length is > 0 and < 6000 && !texto.Contains(cuerpo, StringComparison.Ordinal))
                    {
                        texto += "\n" + cuerpo;
                    }
                }

                var suyos = clientes.Where(c => Regex.IsMatch(texto, @"\b" + c + @"\b")).ToList();
                if (suyos.Count == 0) continue; // no cambia nada por la red: no es un punto de cableado

                var seccion = clave[..^":Mode".Length];

                // El modo cableado sale de la COMPARACIÓN, esté en la condición o en el ayudante
                // que la condición llama (`EsModoApi`, SeamComposer.Tracking.cs).
                var finCond = src.IndexOf('\n', src.IndexOf(')', marca.Index) is var p and >= 0 ? p : marca.Index);
                var cond = src[inicio..(finCond > inicio ? finCond : Math.Min(src.Length, inicio + 300))];
                var modo = Regex.Match(cond, @"""(Api|Bff|Sessions|Http|FileSystem)""").Groups[1].Value;
                if (modo.Length == 0)
                {
                    foreach (var ayudante in Regex.Matches(cond, @"\b([A-Za-z][A-Za-z0-9]{3,})\s*\(")
                                 .Select(m => m.Groups[1].Value))
                    {
                        var hit = Regex.Match(Cuerpo(src, ayudante), @"""(Api|Bff|Sessions|Http)""");
                        if (hit.Success) { modo = hit.Groups[1].Value; break; }
                    }
                }

                string? poco = null;
                foreach (var (_, otro) in composers)
                {
                    var m = Regex.Match(otro,
                        @"Configure<([A-Za-z]+Settings)>\s*\([^;]{0,200}?GetSection\(""" + Regex.Escape(seccion) + @"""\)");
                    if (m.Success) { poco = m.Groups[1].Value; break; }
                }

                var campos = Array.Empty<string>().ToList();
                string? porDefecto = null;
                if (poco is not null && pocos.TryGetValue(poco, out var fuente))
                {
                    campos = new[] { "Mode", "BaseUrl", "ApiKey", "TimeoutSeconds" }
                        .Where(c => Regex.IsMatch(fuente, @"\b" + c + @"\s*\{\s*get")).ToList();
                    var d = Regex.Match(fuente, @"Mode\s*\{\s*get;\s*init;\s*\}\s*=\s*""([A-Za-z]+)""");
                    porDefecto = d.Success ? d.Groups[1].Value : null;
                }

                encontrados.Add(new Punto(
                    clave, seccion, seccion.Split(':')[1], modo, nombre, texto, poco, campos, porDefecto, suyos));
            }
        }

        return encontrados;
    }

    /// <summary>Los que siguen el molde: los que hablan el vocabulario del molde.</summary>
    private static IReadOnlyList<Punto> DelMolde()
        => Puntos().Where(p => Vocabulario.Contains(p.ModoCableado, StringComparer.Ordinal)).ToList();

    // ── Que el gate VE ──────────────────────────────────────────────────────

    [Fact]
    public void El_descubrimiento_ve_los_verticales_que_sabemos_que_existen()
    {
        // Va primero y a propósito, como el de WebhookGateTests. Sin él, romper el
        // descubrimiento —un composer que cambie de idioma, un `if` que pase a `switch`— deja
        // TODOS los demás en verde sobre una lista vacía, y la señal que el equipo lee es
        // «el molde se cumple» cuando lo que pasó es que nadie lo miró.
        var puntos = DelMolde();

        var ausentes = VerticalesConocidos
            .Where(v => !puntos.Any(p => p.Vertical.Equals(v, StringComparison.Ordinal)))
            .ToList();

        Assert.True(ausentes.Count == 0,
            "El descubrimiento no ve estos verticales: " + string.Join(", ", ausentes)
            + ". O se les quitó el cableado —y entonces hay que borrarlos de VerticalesConocidos "
            + "con su razón— o el descubrimiento de MoldeDelVerticalTests dejó de funcionar y el "
            + "resto de este gate está pasando en verde sobre nada. "
            + "Visto hoy: " + string.Join(", ", puntos.Select(p => p.Clave)));
    }

    // ── Los esenciales del doc 12 ───────────────────────────────────────────

    [Fact]
    public void El_vocabulario_del_molde_es_Api_o_Bff()
    {
        // Las dos palabras NO son estilo: son las dos formas del eje transaccional, y elegir
        // entre ellas ES contestar la primera de las tres preguntas. `Api` dice «no hay nada que
        // deshacer, hablo con la capacidad de frente»; `Bff` dice «hay algo que deshacer, y el
        // orden de los pasos no vive acá». Una tercera palabra deja el vertical fuera del molde
        // sin que nadie lo decida — y sin que este gate lo mire.
        //
        // TRINQUETE, no lista de excepciones: los dos de hoy son anteriores al molde y están
        // nombrados en PuntosAnterioresAlMolde con su razón.
        var fuera = Puntos()
            .Where(p => !Vocabulario.Contains(p.ModoCableado, StringComparer.Ordinal))
            .ToList();

        Assert.True(fuera.Count <= PuntosAnterioresAlMolde,
            $"Hay {fuera.Count} puntos de cableado fuera del vocabulario del molde y el trinquete "
            + $"está en {PuntosAnterioresAlMolde}: "
            + string.Join(", ", fuera.Select(p => $"{p.Clave}=\"{p.ModoCableado}\""))
            + ". Si es una capacidad, se llama \"Api\"; si es un orquestador, \"Bff\". Si de verdad "
            + "no es ninguna de las dos, lo que se queda corto es docs/product/12-el-molde-de-un-"
            + "vertical.md y hay que escribirlo ahí — no inventar una palabra en un composer.");
    }

    [Fact]
    public void Cada_punto_de_cableado_ENLAZA_su_seccion()
    {
        // Sin el Configure<>, el cliente recibe un <X>Settings recién construido y todo lo que NO
        // viaja por el HttpClient —los Kind del sujeto, el prefijo de la definición, el margen de
        // renovación— se queda en su default EN SILENCIO: configurarlo no hace nada y nadie sabe
        // por qué. Es el olvido que arrastraron Tienda (#24), Salud (#25), Viajes (#36) y las
        // notificaciones de Gobierno (#62): CUATRO verticales, que es lo que lo convierte en
        // esencial del molde y no en un descuido.
        //
        // Y los cuatro campos son los cuatro: sin `TimeoutSeconds` el despliegue no puede decidir
        // cuánto espera, y ese número no es cosmético — comprar cruza seis servicios y cortar
        // pronto no evita el problema, solo lo hace más probable.
        var mal = DelMolde()
            .Where(p => p.Poco is null || p.CamposDelPoco.Count < 4)
            .Select(p => p.Poco is null
                ? $"{p.Clave} (sin Configure<…Settings>(GetSection(\"{p.Seccion}\")))"
                : $"{p.Clave} → {p.Poco} le faltan {string.Join('/', new[] { "Mode", "BaseUrl", "ApiKey", "TimeoutSeconds" }.Except(p.CamposDelPoco))}")
            .ToList();

        Assert.True(mal.Count == 0, "Puntos de cableado sin sección enlazada o incompleta: " + string.Join("; ", mal));
    }

    [Fact]
    public void El_default_NUNCA_es_el_valor_cableado()
    {
        // El camino del clon limpio. Un repo recién bajado tiene que levantar y VENDER sin
        // levantar seis servicios; si el default fuera el valor cableado, «no configurado»
        // pasaría a significar «roto», y lo primero que vería alguien nuevo sería un vertical
        // caído por una razón que no tiene nada que ver con su código.
        var mal = DelMolde()
            .Where(p => p.DefaultDelPoco is not null
                     && p.DefaultDelPoco.Equals(p.ModoCableado, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Clave} ({p.Poco}.Mode = \"{p.DefaultDelPoco}\")")
            .ToList();

        Assert.True(mal.Count == 0,
            "Estos puntos arrancan cableados por defecto: " + string.Join(", ", mal)
            + ". El default es el camino EN PROCESO — Stub, Local, Engine — y cambiarlo convierte "
            + "un clon limpio en un despliegue roto.");
    }

    [Fact]
    public void En_modo_Api_un_interruptor_gobierna_UNA_capacidad()
    {
        // ESTE es el que hace difícil el error que CLAUDE.md §11 documenta CUATRO veces seguidas
        // —StubClinicalSchedulingService, StubVisitSchedulingService, StubReturnService y
        // StubApplicationService—, y siempre por el mismo motivo: se mira «cuántos pasos compone»
        // y se concluye «orquestador». Componer no es orquestar.
        //
        // La huella en el árbol de contestar mal es SIEMPRE la misma y sí se puede medir: dos
        // clientes de capacidades distintas colgando de un solo interruptor directo. Si cuelgan
        // del mismo interruptor es que alguien los considera UN flujo — y un flujo con dos pasos
        // que pueden fallar a la mitad tiene algo que deshacer, o sea es un orquestador.
        //
        // La salida no es siempre «hacelo Bff»: Gobierno tiene TRES interruptores directos
        // (Gob, Gob:Notifications, Gob:Payments) precisamente porque notificar es Api.Messaging y
        // decidir es Api.Workflow, y juntarlos obligaría a encender los dos para probar uno.
        var mal = DelMolde()
            .Where(p => p.ModoCableado.Equals("Api", StringComparison.Ordinal) && p.Clientes.Count > 1)
            .Select(p => $"{p.Clave} → {string.Join(" + ", p.Clientes)}")
            .ToList();

        Assert.True(mal.Count == 0,
            "Un interruptor en modo \"Api\" gobierna más de una capacidad: " + string.Join("; ", mal)
            + ". Las tres preguntas, por separado: ¿hay algo que DESHACER si un paso falla? → si "
            + "sí, es un orquestador y el modo es \"Bff\". ¿El recurso lo lleva ALGUIEN MÁS? → "
            + "decide si hay que cablearlo, no a qué nivel. ¿QUIÉN TIENE LA PLATA? → decide a "
            + "quién se le pide el movimiento. Si de verdad no hay nada que deshacer, son dos "
            + "capacidades independientes y llevan DOS interruptores, como Gobierno.");
    }

    [Fact]
    public void Cada_punto_de_cableado_propaga_la_correlacion()
    {
        // Sin esto la compra deja un rastro en el CMS y otro distinto en el orquestador, y son la
        // misma compra: la pregunta que la HU #28 vino a contestar —«mostrame todo lo de esta
        // compra»— vuelve a no tener respuesta.
        //
        // Se CUENTA por punto en vez de enumerar composers, y no es un detalle de estilo:
        // CorrelationTests lo vigila con una lista de CUATRO ficheros escrita a mano, así que un
        // vertical nuevo que no propague pasa en verde. Es «una lista sacada de la cabeza en vez
        // de medida contra el fichero», que CLAUDE.md documenta cuatro veces. Aquel test no se
        // borra —cubre los dos árboles, y esto solo cubre el CMS— pero este cierra su hueco.
        //
        // Los webhooks hacia TERCEROS quedan fuera solos, sin lista: no cuelgan de un
        // `Synergos:…:Mode`, así que el descubrimiento no los ve. A un tercero conviene mandarle
        // lo mínimo.
        var mal = DelMolde()
            .Where(p => Regex.IsMatch(p.Rama, @"AddHttpClient\s*\(")
                     && !p.Rama.Contains("AddHttpMessageHandler<CorrelationForwardingHandler>()", StringComparison.Ordinal))
            .Select(p => $"{p.Clave} ({p.Composer})")
            .ToList();

        Assert.True(mal.Count == 0,
            "Estos puntos de cableado no propagan X-Correlation-Id: " + string.Join(", ", mal)
            + ". Con que un solo salto lo corte, el rastro se parte en dos historias.");
    }

    [Fact]
    public void Cada_punto_de_cableado_manda_la_llave_compartida()
    {
        // Todo el árbol de servicios va detrás de la llave compartida —las dos excepciones son
        // /health y los webhooks de terceros, y ninguna de las dos cuelga de un interruptor—. Un
        // cliente sin llave no falla al arrancar: sirve, y la capacidad le contesta 401 a la
        // primera persona que intente comprar. Es la forma del #56 y la de la llave de firma de
        // Api.Identity — arrancar verde y reventar delante de alguien es el peor de los tres.
        var mal = DelMolde()
            .Where(p => !Regex.IsMatch(p.Rama, @"ApiKeyHeader|X-Synergos-Key"))
            .Select(p => $"{p.Clave} ({p.Composer})")
            .ToList();

        Assert.True(mal.Count == 0,
            "Estos puntos salen a la red sin la llave compartida: " + string.Join(", ", mal));
    }

    [Fact]
    public void Cada_punto_de_cableado_tiene_un_gate_que_nombra_su_cliente()
    {
        // Lo que este gate NO puede comprobar es lo propio de cada vertical: que la cita no
        // adivine el identificador del recurso (#25), que la visita toque UNA capacidad (#33a),
        // que el acto no se pueda leer sin registrar el acceso (#62), que la bitácora se repita
        // sin firmar y la canasta no (#14/#72). Eso lo escribe cada vertical en SU gate, y por
        // eso el molde exige que ese gate exista: un vertical cableado sin nadie que lo vigile es
        // exactamente el caso que el descubrimiento por *WiringTests no habría podido ver.
        //
        // Se busca por el nombre del CLIENTE y no por el del vertical a propósito: el gate de
        // Tienda se llama ShopWiringTests, y un gate que cruzara por nombre lo daría por ausente.
        //
        // Y se busca CON FRONTERA DE PALABRA y SIN COMENTARIOS, que no es puntillismo: la
        // primera versión usaba Contains() sobre el texto crudo y una mutación la pasó en verde
        // —renombrar el cliente a `HttpCertificateIdSignerX` seguía conteniendo el nombre viejo—.
        // Es el punto ciego que feedback_a_gate_that_parses_source_needs_its_own_mutations
        // describe: sale un número plausible y nadie lo cruza.
        var arch = Directory.EnumerateFiles(Dir("Synergos.CMS.Tests", "Architecture"), "*.cs")
            .Where(f => !f.EndsWith("MoldeDelVerticalTests.cs", StringComparison.Ordinal))
            .Select(SinComentarios)
            .ToList();

        var mal = DelMolde()
            .Where(p => !arch.Any(t => p.Clientes.Any(c => Regex.IsMatch(t, @"\b" + c + @"\b"))))
            .Select(p => $"{p.Clave} ({string.Join(", ", p.Clientes)})")
            .ToList();

        Assert.True(mal.Count == 0,
            "Estos puntos de cableado no los vigila ningún gate de Tests/Architecture/: "
            + string.Join("; ", mal)
            + ". El molde comprueba la FORMA; lo que el vertical rechaza y por qué lo tiene que "
            + "escribir él.");
    }

    /// <summary>
    /// Los SIETE verticales tienen el eje 1: su catálogo sale del contenido del CMS.
    /// </summary>
    /// <remarks>
    /// <b>Este gate no existía, y el doc 12 §7.2 explicaba por qué</b>: Salud era el único
    /// vertical sin DocType y sin fuente de contenido —el profesional salía de un stub sembrado
    /// en C#, así que dar de alta un médico era un cambio de código y un despliegue— y exigirlo
    /// habría dejado el build rojo por una decisión de producto que nadie había tomado. Un gate
    /// siempre rojo deja de leerse. La decisión se tomó en el #118 y la excepción se fue con
    /// ella.
    ///
    /// <para><b>El cruce es por COMPOSER, y conviene saber por qué no es por nombre.</b> Los dos
    /// ejes hablan vocabularios distintos: el interruptor dice <c>Tienda</c>, <c>Gob</c>,
    /// <c>Viajes</c>, <c>Eventos</c> y la fuente dice <c>Shop</c>, <c>Gov</c>, <c>Booking</c>,
    /// <c>Events</c>. Escribir a mano esa tabla de siete filas es exactamente lo que §2 dice que
    /// congela un error. Lo que sí está en el disco es que <b>el composer parcial ES el cableado
    /// del vertical</b>: los puntos de Academy viven en <c>SeamComposer.Academy.cs</c> y su
    /// fuente también. Así que se agrupa por fichero y se exige que registre al menos tantas
    /// fuentes distintas como verticales cablea.</para>
    ///
    /// <para><b>Y lo que ese corte NO ve, dicho para que nadie confíe de más:</b> dentro de un
    /// composer que cablea varios verticales —hoy sólo <c>SeamComposer.EventsPropertiesGov.cs</c>,
    /// con tres— el gate cuenta, no empareja. Tres fuentes y tres verticales cruzan aunque
    /// estuvieran mal repartidos. Afinarlo exigiría la tabla de nombres que este comentario acaba
    /// de descartar; se queda escrito porque un gate que se cree más listo de lo que es es peor
    /// que no tenerlo.</para>
    ///
    /// <para><b>Se vio en rojo</b> quitándole a <c>SeamComposer.PlatformAndHealthcare.cs</c> el
    /// registro de <c>UmbracoProfessionalDirectorySource</c>, que es el árbol tal como estaba
    /// antes del #118: «Salud (SeamComposer.PlatformAndHealthcare.cs): 1 vertical, 0 fuentes».</para>
    /// </remarks>
    [Fact]
    public void Cada_vertical_tiene_su_EJE_1()
    {
        var fuentes = Directory
            .EnumerateFiles(Dir("Synergos.CMS.Web", "Services", "Catalog"), "Umbraco*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

        Assert.True(fuentes.Count >= 6,
            "El descubrimiento de fuentes de catálogo no ve nada (" + fuentes.Count + "): "
            + "si se movieron de carpeta, este test pasa en verde sin mirar nada.");

        var sinEje1 = new List<string>();

        foreach (var grupo in DelMolde()
                     .Where(p => VerticalesConocidos.Contains(p.Vertical, StringComparer.Ordinal))
                     .GroupBy(p => p.Composer, StringComparer.Ordinal))
        {
            var verticales = grupo
                .Select(p => p.Vertical)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(v => v, StringComparer.Ordinal)
                .ToList();

            // Vale cualquiera de las dos formas de elegir la fuente: `IsCmsSource(sp, X.Vertical)`
            // —la de cinco— y la lectura directa de `Sources` de la Tienda, que es anterior al
            // ayudante. Lo que las dos comparten, y es lo que se mide, es NOMBRAR la fuente.
            var codigo = SinComentarios(Dir("Synergos.CMS.Web", "Composers", grupo.Key));
            var registradas = fuentes
                .Where(f => Regex.IsMatch(codigo, @"\b" + f + @"\b"))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (registradas.Count < verticales.Count)
            {
                sinEje1.Add($"{string.Join(" + ", verticales)} ({grupo.Key}): "
                    + $"{verticales.Count} vertical(es), {registradas.Count} fuente(s)");
            }
        }

        Assert.True(sinEje1.Count == 0,
            "Estos verticales no tienen el eje 1 del molde: " + string.Join("; ", sinEje1)
            + ". El catálogo de un vertical lo autora un editor y lo sirve el árbol de contenido "
            + "(doc 12 §3): un DocType, un Umbraco<X>Source, sus <X>ContentRules y un "
            + "Synergos:Catalog:Sources:<X> que vale demo o cms. Sin él, dar de alta el objeto "
            + "central del vertical es un cambio de código y un despliegue.");
    }

    [Fact]
    public void El_catalogo_de_un_vertical_NO_sale_a_la_red()
    {
        // El otro eje, y el error caro de la épica (doc 11, familia B): cablear el CATÁLOGO a una
        // capacidad sería un RETROCESO. El dato ya tiene dueño —lo autora un editor en el
        // backoffice y lo sirve el árbol de contenido—, así que meter una llamada HTTP en medio
        // cambia una lectura en proceso por una ida a la red Y le quita al editor la superficie
        // donde publica.
        //
        // Se mide sobre las fuentes, no sobre la prosa: un `HttpClient` dentro de un
        // Umbraco*Source es la señal inequívoca de que alguien confundió los dos ejes.
        var fuentes = Directory.EnumerateFiles(Dir("Synergos.CMS.Web", "Services", "Catalog"), "Umbraco*.cs").ToList();

        Assert.True(fuentes.Count >= 5,
            "El descubrimiento de fuentes de catálogo no ve nada (" + fuentes.Count + "): "
            + "si se movieron de carpeta, este test pasa en verde sin mirar nada.");

        var mal = fuentes
            .Where(f => Regex.IsMatch(SinComentarios(f), @"\bHttpClient\b|\bIHttpClientFactory\b"))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(mal.Count == 0,
            "Estas fuentes de catálogo salen a la red: " + string.Join(", ", mal)
            + ". El catálogo de un vertical sale del contenido de Umbraco "
            + "(Synergos:Catalog:Sources:<X> = demo|cms) y NO de una capacidad.");
    }
}
