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

    private static string Dir(params string[] partes) => Proyectos.Ruta(partes);

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
                || t.StartsWith('*')
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
            .ToDictionary(f => Path.GetFileNameWithoutExtension(f)!, f => File.ReadAllText(f), StringComparer.Ordinal);

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
        var arch = Directory.EnumerateFiles(Dir("Synergos.Arquitectura.Tests", "Architecture"), "*.cs")
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

    /// <summary>
    /// Toda fuente de catálogo que existe está REGISTRADA por algún composer.
    /// </summary>
    /// <remarks>
    /// <para><b>Hace falta porque el diente de arriba no puede ver un vertical sin eje 2</b>, y
    /// eso se descubrió construyendo el octavo (#146). <c>Cada_vertical_tiene_su_EJE_1</c>
    /// recorre <c>DelMolde()</c>, que descubre verticales por su <b>interruptor de
    /// transacción</b>; Social contestó «¿hay algo que deshacer?» con NO, así que no tiene
    /// ninguno y el gate del eje 1 no lo mira. O sea que el gate del PRIMER eje estaba acoplado
    /// al descubrimiento del SEGUNDO, y un vertical que sólo tuviera catálogo podía quedarse sin
    /// fuente sin que nada se pusiera rojo.</para>
    ///
    /// <para>Se deriva del disco por los dos lados —las fuentes que hay y los composers que las
    /// nombran— así que crece solo con el catálogo y no lleva lista que mantener.</para>
    /// </remarks>
    [Fact]
    public void Toda_fuente_de_catalogo_esta_registrada_por_un_composer()
    {
        var fuentes = Directory
            .EnumerateFiles(Dir("Synergos.CMS.Web", "Services", "Catalog"), "Umbraco*.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(fuentes.Count >= 8,
            "El descubrimiento de fuentes de catálogo ve " + fuentes.Count + ": si se movieron de "
            + "carpeta, este test pasa en verde sin mirar nada.");

        var composers = string.Join(
            '\n',
            Directory.EnumerateFiles(Dir("Synergos.CMS.Web", "Composers"), "SeamComposer*.cs")
                .Select(SinComentarios));

        var huerfanas = fuentes
            .Where(f => !Regex.IsMatch(composers, @"\b" + f + @"\b"))
            .ToList();

        Assert.True(huerfanas.Count == 0,
            "Estas fuentes de catálogo no las registra ningún composer: "
            + string.Join(", ", huerfanas)
            + ". Una fuente que nadie enchufa no es el eje 1 de nadie: es código que no se "
            + "ejecuta, y el editor sigue sin poder publicar (#146).");
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

    // ── EJE 3 · el ARTEFACTO (doc 12 §3, §5.7 y §5.8) ───────────────────────

    /// <summary>
    /// El censo del eje 3 vive en la TABLA del doc 12 §3, y este gate la lee.
    /// </summary>
    /// <remarks>
    /// <para><b>Por qué el censo es la tabla y no una lista aquí.</b> No hay nada en el disco que
    /// diga «esta clase es el artefacto de Gobierno»: el nombre no lo delata
    /// (<c>StubApplicationService</c>) y el <c>ResourceType</c> tampoco pertenece a nadie. Así que
    /// la atribución es inevitablemente una decisión escrita — y lo que un gate SÍ puede hacer es
    /// impedir que esa decisión se desvíe, cruzándola contra el disco en los dos sentidos.</para>
    ///
    /// <para><b>Y hacía falta:</b> hasta el #153 esa sección nombraba CINCO ejemplos y afirmaba
    /// «los siete lo tienen» — una cifra sobre una lista incompleta, que es
    /// <c>feedback_a_named_list_beats_a_count</c> dentro del documento que predica medir. Faltaban
    /// Salud y Viajes, y el de Salud es el interesante: no guarda por <c>IJsonEntityStore</c> sino
    /// por <c>IPhiStore</c>, porque el dato es clínico.</para>
    /// </remarks>
    [Fact]
    public void Cada_vertical_tiene_su_EJE_3()
    {
        var censo = CensoDelArtefacto();

        Assert.True(censo.Count >= 7,
            "El censo del eje 3 no se pudo leer del doc 12 §3 (" + censo.Count + " fila(s)): "
            + "si la tabla se movió o cambió de forma, este gate pasa en verde sin mirar nada.");

        var sinFila = VerticalesConocidos
            .Where(v => !censo.ContainsKey(v))
            .ToList();

        Assert.True(sinFila.Count == 0,
            "Estos verticales no tienen fila en la tabla del eje 3 (doc 12 §3): "
            + string.Join(", ", sinFila) + ". Un vertical sin artefacto es una DECISIÓN —hay que "
            + "escribirla con su razón—, no un hueco que se deja en blanco.");

        var sobran = censo.Keys
            .Where(v => !VerticalesConocidos.Contains(v, StringComparer.Ordinal))
            .ToList();

        Assert.True(sobran.Count == 0,
            "La tabla del eje 3 nombra verticales que ya no existen: " + string.Join(", ", sobran)
            + ". Un censo vigilado en un solo sentido acaba afirmando lo que ya no está (#137).");

        var malas = new List<string>();
        foreach (var (vertical, fila) in censo.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            var clases = fila.Clases;
            var fuentes = clases.Select(c => (Clase: c, Ruta: FuenteDe(c))).ToList();

            foreach (var f in fuentes.Where(f => f.Ruta is null))
            {
                malas.Add($"{vertical}: «{f.Clase}» no existe en el disco");
            }

            // Lo que hace que un registro sea un registro es que SOBREVIVA al proceso. Una de las
            // clases de la fila tiene que tocar un almacén durable; las demás pueden ser
            // proyecciones (`VisitAgenda`) o el aviso, que no guardan nada.
            var durables = fuentes
                .Where(f => f.Ruta is not null)
                .Where(f => Regex.IsMatch(SinComentarios(f.Ruta!), @"\bIJsonEntityStore\b|\bIPhiStore\b"))
                .ToList();

            // Y el caso que de verdad muerde, porque pasa el anterior en verde: que el registro
            // del artefacto SEA el seam del eje 2. Ahí lo durable existe sólo en el modo en
            // proceso —el cliente cableado no guarda nada de este lado—, así que con el modo
            // encendido el vertical deja de cumplir la promesa de §3: «la prueba se puede ver con
            // el otro árbol caído». Se mide cruzando el nombre contra los clientes `Http*` que el
            // composer de ese vertical registra, sin lista a mano.
            var cableados = DelMolde()
                .Where(p => string.Equals(p.Vertical, vertical, StringComparison.Ordinal))
                .SelectMany(p => p.Clientes)
                .Select(c => c.StartsWith("Http", StringComparison.Ordinal) ? c[4..] : c)
                .ToHashSet(StringComparer.Ordinal);

            var fundidas = clases
                .Select(c => (Clase: c, Desnudo: Regex.Replace(c, "^(Stub|FileSystem|Hmac)", string.Empty)))
                .Where(x => cableados.Contains(x.Desnudo))
                .Select(x => x.Clase)
                .ToList();

            if (fundidas.Count > 0 && !Regex.IsMatch(fila.Fila, @"#\d+"))
            {
                malas.Add($"{vertical}: [{string.Join(", ", fundidas)}] es a la vez el registro del "
                    + "artefacto y el seam del eje 2, así que con el modo cableado no queda nada "
                    + "de este lado — y la fila no nombra el ticket que lo cierra");
            }

            // Un eje 3 que no guarda nada NO es un hueco que se deja en blanco: o es una
            // decisión, o es un ticket. Y la diferencia está escrita
            // (`feedback_a_census_entry_is_how_a_defect_survives_its_own_gate`): una razón que
            // contesta «por qué esto no se arregló TODAVÍA» es un ticket sin abrir disfrazado de
            // exención, así que la fila tiene que NOMBRARLO.
            if (fuentes.All(f => f.Ruta is not null) && durables.Count == 0
                && !Regex.IsMatch(fila.Fila, @"#\d+"))
            {
                malas.Add($"{vertical}: ninguna de [{string.Join(", ", clases)}] guarda nada "
                    + "durable y la fila no nombra el ticket que lo cierra");
            }
        }

        Assert.True(malas.Count == 0,
            "El eje 3 de estos verticales no cuadra con el disco: " + string.Join("; ", malas)
            + ". El artefacto es lo que queda como PRUEBA (doc 12 §3): si no sobrevive al proceso "
            + "no es una prueba, es una pantalla.");
    }

    /// <summary>
    /// El registro de un artefacto NUNCA sale a la red — es lo único que sostiene «la prueba se
    /// puede ver con el otro árbol caído» (doc 12 §3). El gemelo exacto de
    /// <see cref="El_catalogo_de_un_vertical_NO_sale_a_la_red"/>, un eje más allá.
    /// </summary>
    /// <remarks>
    /// <para>Lo que SÍ puede cruzar es el <b>sello</b>: <c>HttpCertificateIdSigner</c> existe desde
    /// el #45 y lo que mudó a <c>Api.Signing</c> fue la CUSTODIA de la llave, no el índice de
    /// emitidos. Por eso el gate mide el registro y no toca al firmante.</para>
    ///
    /// <para><b>Y NO lleva un diente de «tiene gemelo <c>Http*</c>», aunque es lo primero que se
    /// escribe.</b> Lo tuvo, y su primer arranque marcó a Realty: ahí el registro del artefacto y
    /// el seam del eje 2 son <b>la misma clase</b> (<c>StubVisitSchedulingService</c>), así que el
    /// gemelo existe por el eje 2 y el diente no sabe distinguirlo. Un gate que no puede separar
    /// los dos casos marca el legítimo y enseña a ignorarlo. Lo que ese diente creía medir —que
    /// con el otro árbol caído quede algo de este lado— es real y en Realty <b>no se cumple</b>;
    /// va como fila del censo con su ticket, no como falso positivo aquí.</para>
    /// </remarks>
    [Fact]
    public void El_registro_de_un_artefacto_NO_sale_a_la_red()
    {
        var censo = CensoDelArtefacto();

        Assert.True(censo.Count >= 7,
            "El censo del eje 3 no se pudo leer del doc 12 §3 (" + censo.Count + " fila(s)).");

        var mal = new List<string>();
        foreach (var (vertical, fila) in censo.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var clase in fila.Clases)
            {
                var ruta = FuenteDe(clase);
                if (ruta is null) continue;

                if (Regex.IsMatch(SinComentarios(ruta), @"\bHttpClient\b|\bIHttpClientFactory\b"))
                {
                    mal.Add($"{vertical}/{clase}");
                }
            }
        }

        Assert.True(mal.Count == 0,
            "Estos registros de artefacto salen a la red: " + string.Join(", ", mal)
            + ". El artefacto se queda en el CMS (doc 12 §3): con el otro árbol caído se sigue "
            + "viendo «mis entradas», se sigue transfiriendo y se sigue escaneando en la puerta.");
    }

    /// <summary>
    /// El SELLO (doc 12 §5.8): su implementación en proceso es la de VERDAD —se llama
    /// <c>Hmac*</c>, no <c>Stub*</c>— y su llave se guarda CIFRADA.
    /// </summary>
    /// <remarks>
    /// <para>Se descubre del disco —todo <c>I*Signer</c> de <c>Interfaces</c>— y no de una lista,
    /// porque el tercero que aparezca tiene que entrar solo.</para>
    ///
    /// <para><b>Lo que mide el cifrado no es que la llave exista.</b> Un
    /// <c>&lt;X&gt;SigningKeyProvider</c> que generara la llave y la escribiera en claro dejaría
    /// en el disco del servidor lo único que hace falta para fabricar un diploma con el nombre de
    /// quien sea — y no fallaría nunca, porque firmaría igual de bien.</para>
    /// </remarks>
    [Fact]
    public void El_sello_de_un_artefacto_guarda_su_llave_cifrada()
    {
        var seams = Directory
            .EnumerateFiles(Dir("Synergos.CMS.Interfaces"), "I*Signer.cs")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(seams.Count >= 2,
            "El descubrimiento de seams de sello no ve nada (" + seams.Count + "): "
            + "si se renombraron, este gate pasa en verde sin mirar nada.");

        var mal = new List<string>();
        foreach (var seam in seams)
        {
            var sinI = seam[1..];

            if (FuenteDe("Hmac" + sinI) is null)
            {
                mal.Add($"{seam}: no hay Hmac{sinI} — la del proceso es la de VERDAD y se llama así");
            }

            if (FuenteDe("Stub" + sinI) is not null)
            {
                mal.Add($"{seam}: hay un Stub{sinI} — un firmante en proceso no es un doble de nada");
            }
        }

        var custodios = Directory
            .EnumerateFiles(Dir("Synergos.CMS.Web", "Services"), "*SigningKeyProvider.cs")
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        Assert.True(custodios.Count >= 2,
            "El descubrimiento de custodios de llave no ve nada (" + custodios.Count + ").");

        foreach (var c in custodios)
        {
            if (!Regex.IsMatch(SinComentarios(c), @"\bIDataProtector\b|\bIDataProtectionProvider\b"))
            {
                mal.Add($"{Path.GetFileName(c)}: guarda la llave SIN cifrar");
            }
        }

        Assert.True(mal.Count == 0,
            "El sello del eje 3 no cumple el molde: " + string.Join("; ", mal)
            + " (doc 12 §5.8). El sello es lo único que hace que escribir en el almacén no alcance "
            + "para fabricar el artefacto.");
    }

    /// <summary>
    /// La tabla del eje 3 del doc 12 §3, leída: vertical → las clases que su tercera columna
    /// nombra entre acentos graves.
    /// </summary>
    private static IReadOnlyDictionary<string, (IReadOnlyList<string> Clases, string Fila)> CensoDelArtefacto()
    {
        var doc = File.ReadAllLines(
            Dir("Synergos.CMS.Web", "docs", "product", "12-el-molde-de-un-vertical.md"));

        var censo = new Dictionary<string, (IReadOnlyList<string> Clases, string Fila)>(StringComparer.Ordinal);

        // Se recorta la SECCIÓN, no el documento. El doc 12 §2 tiene otra tabla cuyas filas
        // empiezan igual —`| **Tienda** |`— y cuya tercera columna es el Controller: un parser
        // que leyera el fichero entero se quedaría con ésa y afirmaría que el artefacto de Tienda
        // es `ShopController`. Lo destapó el primer arranque del gate, no leerlo.
        var dentro = false;
        foreach (var linea in doc)
        {
            if (linea.StartsWith("### Eje 3", StringComparison.Ordinal)) { dentro = true; continue; }
            if (dentro && linea.StartsWith('#')) break;
            if (!dentro) continue;

            var celdas = linea.Split('|', StringSplitOptions.TrimEntries);
            // | vacío | vertical | artefacto | quién | almacén | sello | vacío
            if (celdas.Length < 7) continue;

            var cabecera = celdas[1];
            var vertical = VerticalesConocidos.FirstOrDefault(v =>
                cabecera.Contains("`" + v + "`", StringComparison.Ordinal)
                || cabecera.StartsWith("**" + v + "**", StringComparison.Ordinal));
            if (vertical is null || censo.ContainsKey(vertical)) continue;

            var clases = Regex.Matches(celdas[3], "`([A-Za-z][A-Za-z0-9_]*)`")
                .Select(m => m.Groups[1].Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (clases.Count == 0) continue;

            censo[vertical] = (clases, linea);
        }

        return censo;
    }

    /// <summary>El fichero de una clase del árbol del CMS, o <c>null</c> si no existe.</summary>
    private static string? FuenteDe(string clase)
    {
        foreach (var dir in new[]
                 {
                     Dir("Synergos.CMS.Application", "Services", "Impl"),
                     Dir("Synergos.CMS.Application", "Services"),
                     Dir("Synergos.CMS.Web", "Services"),
                 })
        {
            var ruta = Path.Combine(dir, clase + ".cs");
            if (File.Exists(ruta)) return ruta;
        }

        return null;
    }
}
