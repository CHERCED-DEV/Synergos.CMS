using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que ningún webhook escriba sin haber comprobado quién lo manda (HU #27).
/// </summary>
/// <remarks>
/// <para><b>Un webhook es la única superficie de una capacidad que NO va detrás de la llave
/// compartida</b>, porque quien lo llama es un tercero que no la tiene. Lo que lo protege es su
/// firma, y nada más. Sin ella, cualquiera que sepa la URL marca un cobro como pagado —y el
/// pedido sale— o da por entregado un aviso que rebotó; en Gobierno, «entregado» es lo que hace
/// correr un término.</para>
///
/// <para><b>El defecto ya se vio y por eso este gate existe.</b> <c>Api.Notifications</c> mutó el
/// suyo quitando la verificación del lambda del endpoint y <i>no falló ni un test</i>: los del
/// verificador probaban el verificador, y ninguno probaba que alguien lo llamara. Es la forma
/// exacta del <c>feedback_contract_shape_needs_its_own_test</c> — lo que hay que vigilar no es que
/// la pieza exista, es que esté <b>enchufada</b>.</para>
///
/// <para><b>Vigila las DOS capacidades que reciben eventos</b>, no solo la última. Un gate escrito
/// alrededor del caso que lo motivó deja al otro sin vigilar y nadie se entera hasta que alguien
/// lo toca.</para>
///
/// <para><b>Y desde #114 vigila también LA PUERTA, no sólo la cerradura.</b> Que un webhook
/// verifique su firma no sirve de nada si no puede llegar: el <c>Caddyfile</c> mandaba
/// <c>/v1/webhooks/*</c> entero a <c>api-notifications</c> —escrito cuando el webhook era uno
/// solo (ADR 0131)— y la HU #27 añadió el de Wompi en <c>Api.Payments</c>. Wompi recibía
/// <b>404 del servicio equivocado</b>, y ningún gate lo miraba.</para>
///
/// <para><b>El gate que había lo CEMENTABA</b>: <c>ComposeStackTests</c> exigía exactamente UNA
/// <c>reverse_proxy</c> hacia el árbol, así que escribir la ruta correcta rompía el build. Contar
/// fue el error — es «una lista sacada de la cabeza en vez de medida contra el fichero», que
/// <c>CLAUDE.md</c> §11 documenta cuatro veces. Lo que se mide ahora es <b>cobertura</b>, en las
/// dos direcciones: todo <c>MapPost("/v1/webhooks/…")</c> que exista tiene quien lo enrute hasta
/// SU capacidad, y toda <c>reverse_proxy</c> hacia el árbol sirve un webhook que existe — que es
/// la razón original del gate viejo, intacta y sin número.</para>
/// </remarks>
public sealed class WebhookGateTests
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

    private static IEnumerable<(string Nombre, string Dir)> Capacidades()
        => Proyectos.Todos("Synergos.Api.")
            .Select(d => (Path.GetFileName(d), d))
            .OrderBy(x => x.Item1, StringComparer.Ordinal);

    private static IEnumerable<string> Fuentes(string dir)
        => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>
    /// Quita comentarios para no medir la prosa que documenta la regla.
    /// </summary>
    /// <remarks>
    /// <b>Sin esto el gate se engaña solo</b>, y este repo ya se tropezó dos veces con lo mismo
    /// (#29 y el de #33a): la explicación de una regla cita justo lo que la regla prohíbe, y el
    /// gate la lee como cumplimiento. Se sustituyen por líneas en blanco para no mover los
    /// desplazamientos que el gate compara.
    /// </remarks>
    private static string SinComentarios(string file)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in File.ReadLines(file))
        {
            var t = line.TrimStart();
            if (t.StartsWith("//", StringComparison.Ordinal)
                || t.StartsWith('*')
                || t.StartsWith("/*", StringComparison.Ordinal))
            {
                sb.AppendLine();
                continue;
            }

            var i = line.IndexOf("//", StringComparison.Ordinal);
            sb.AppendLine(i >= 0 ? line[..i] : line);
        }
        return sb.ToString();
    }

    /// <summary>Las capacidades que exponen alguna ruta bajo <c>/v1/webhooks</c>.</summary>
    private static List<(string Nombre, string Dir)> ConWebhook()
        => Capacidades()
            .Where(c => Fuentes(c.Dir).Any(f =>
                Regex.IsMatch(SinComentarios(f), @"\.MapPost\(\s*""/v1/webhooks")))
            .ToList();

    /// <summary>
    /// Los webhooks que EXISTEN, con la ruta exacta y el servicio que la sirve.
    /// </summary>
    /// <remarks>
    /// Se leen del árbol y no de una lista: el defecto #114 fue exactamente una lista de una
    /// sola entrada —el <c>handle</c> del Caddyfile— escrita cuando el webhook era uno solo.
    /// El nombre del servicio se calcula como lo calcula <c>compose-gen.mjs</c>
    /// (<c>Synergos.Api.Payments</c> → <c>api-payments</c>), que es lo que el proxy nombra.
    /// </remarks>
    private static List<(string Ruta, string Servicio, string Capacidad)> RutasDeWebhook()
        => ConWebhook()
            .SelectMany(c => Fuentes(c.Dir)
                .SelectMany(f => Regex
                    .Matches(SinComentarios(f), @"\.MapPost\(\s*""(/v1/webhooks/[^""]*)""")
                    .Select(m => (
                        Ruta: m.Groups[1].Value,
                        Servicio: c.Nombre.Replace("Synergos.", "").Replace(".", "-").ToLowerInvariant(),
                        Capacidad: c.Nombre))))
            .Distinct()
            .OrderBy(x => x.Ruta, StringComparer.Ordinal)
            .ToList();

    /// <summary>Un bloque <c>handle</c> del Caddyfile: a qué rutas responde y hacia dónde va.</summary>
    private sealed record Bloque(string Matcher, string? Destino, int Linea);

    /// <summary>
    /// Los <c>handle</c> del Caddyfile, con su matcher de ruta y su <c>reverse_proxy</c>.
    /// </summary>
    /// <remarks>
    /// <c>Destino</c> es <c>null</c> cuando el bloque no reenvía a nada (el <c>respond 404</c>
    /// del final). Un bloque <c>handle</c> sin matcher es el catch-all, y se anota como
    /// <c>""</c>: hace falta tenerlo para saber qué pasaría con una ruta sin bloque propio.
    /// </remarks>
    private static List<Bloque> BloquesDelProxy()
    {
        var lineas = File.ReadAllLines(Path.Combine(RepoRoot(), "Caddyfile"));
        var bloques = new List<Bloque>();
        string? matcher = null;
        string? destino = null;
        var linea = 0;
        var profundidad = 0;

        for (var i = 0; i < lineas.Length; i++)
        {
            var t = lineas[i].Trim();
            if (t.StartsWith('#')) continue;

            if (matcher is null)
            {
                var m = Regex.Match(t, @"^handle\s*(\S*)\s*\{$");
                if (!m.Success) continue;
                matcher = m.Groups[1].Value;
                destino = null;
                linea = i + 1;
                profundidad = 1;
                continue;
            }

            profundidad += t.Count(c => c == '{') - t.Count(c => c == '}');

            var rp = Regex.Match(t, @"^reverse_proxy\s+(\S+)");
            if (rp.Success) destino = rp.Groups[1].Value.TrimEnd('{').Trim();

            if (profundidad <= 0)
            {
                bloques.Add(new Bloque(matcher, destino, linea));
                matcher = null;
            }
        }

        return bloques;
    }

    /// <summary>
    /// Qué bloque atiende una ruta, con el criterio de Caddy: gana el matcher más específico.
    /// </summary>
    /// <remarks>
    /// Caddy ordena los <c>handle</c> por especificidad del matcher de ruta y no por el orden
    /// del fichero — una ruta exacta le gana a un comodín, y un comodín más largo le gana a uno
    /// más corto. Se replica acá porque el defecto que este gate vigila es justo de precedencia:
    /// un <c>/v1/webhooks/*</c> que se traga la ruta de otro.
    /// </remarks>
    private static Bloque? Atiende(string ruta, IEnumerable<Bloque> bloques)
        => bloques
            .Where(b => b.Matcher.Length == 0
                     || (b.Matcher.EndsWith('*')
                            ? ruta.StartsWith(b.Matcher[..^1], StringComparison.Ordinal)
                            : string.Equals(ruta, b.Matcher, StringComparison.Ordinal)))
            .OrderByDescending(Especificidad)
            .FirstOrDefault();

    /// <summary>
    /// Cuánto pesa un matcher. El catch-all sin matcher es el MENOS específico, siempre.
    /// </summary>
    /// <remarks>
    /// Escrito al revés la primera vez —<c>""</c> puntuaba como ruta exacta y le ganaba a
    /// <c>/v1/webhooks/*</c>— y el mensaje del gate mutado culpaba al catch-all de un comodín que
    /// sí existía. Lo destapó la mutación: un gate que se ve rojo por la razón equivocada apunta
    /// a otro sitio y hace perder la tarde.
    /// </remarks>
    private static int Especificidad(Bloque b)
        => b.Matcher.Length == 0 ? -1
         : b.Matcher.EndsWith('*') ? b.Matcher.Length - 1
         : b.Matcher.Length + 1000;

    [Fact]
    public void El_gate_ve_las_rutas_de_webhook_y_los_bloques_del_proxy()
    {
        // El descubrimiento primero, como arriba: si el parseo del Caddyfile se rompiera, los
        // dos asserts de abajo recorrerían listas vacías y el build quedaría verde sobre nada.
        var rutas = RutasDeWebhook();
        Assert.Contains(("/v1/webhooks/resend", "api-notifications", "Synergos.Api.Notifications"), rutas);
        Assert.Contains(("/v1/webhooks/wompi", "api-payments", "Synergos.Api.Payments"), rutas);

        var bloques = BloquesDelProxy();
        Assert.True(bloques.Count >= 2, $"El Caddyfile se leyó con {bloques.Count} bloques `handle`.");
        Assert.Contains(bloques, b => b.Matcher.Length == 0);
    }

    [Fact]
    public void Todo_webhook_que_EXISTE_tiene_quien_lo_enrute_hasta_SU_capacidad()
    {
        // ─────────────────────────────────────────────────────────────────────────────────
        // ESTE GATE MIDE COBERTURA, NO CUENTA RUTAS — y esa es toda la lección (#114).
        //
        // Lo que había antes contaba: «exactamente UNA `reverse_proxy` hacia el árbol».
        // Se escribió cuando el webhook era uno (ADR 0131), la HU #27 añadió el de Wompi, y
        // el número dejó de significar lo que decía: el Caddyfile mandaba `/v1/webhooks/*`
        // entero a `api-notifications`, que no mapea `wompi`, así que el proveedor recibía
        // 404 DEL SERVICIO EQUIVOCADO — y la ruta correcta ROMPÍA EL BUILD.
        //
        // Es el patrón que este repo ya documenta cuatro veces: una lista sacada de la
        // cabeza en vez de medida contra el fichero. Se arregla como lo arreglaron
        // `IdentityGateTests` y el de `Api.Consent`: contando lo que hay, no enumerando lo
        // que uno recuerda.
        // ─────────────────────────────────────────────────────────────────────────────────
        var bloques = BloquesDelProxy();
        var malas = new List<string>();

        foreach (var (ruta, servicio, capacidad) in RutasDeWebhook())
        {
            var bloque = Atiende(ruta, bloques);

            if (bloque is null || bloque.Matcher.Length == 0)
            {
                malas.Add($"{ruta} ({capacidad}) → el proxy no tiene bloque propio para esta ruta: "
                          + "cae al catch-all y lo contesta el CMS. El proveedor recibe un 404 con "
                          + "HTML, que registra como «la URL existe y rechaza».");
                continue;
            }

            if (bloque.Destino is null)
            {
                malas.Add($"{ruta} ({capacidad}) → el bloque `handle {bloque.Matcher}` "
                          + $"(Caddyfile:{bloque.Linea}) no reenvía a ningún sitio.");
                continue;
            }

            if (!bloque.Destino.StartsWith($"{servicio}:", StringComparison.Ordinal))
            {
                malas.Add($"{ruta} lo sirve {capacidad} —o sea `{servicio}`— y el proxy lo manda a "
                          + $"`{bloque.Destino}` (Caddyfile:{bloque.Linea}). Ese servicio no mapea la "
                          + "ruta: 404 del servicio equivocado, sin que nada falle ruidosamente (#57).");
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void El_proxy_no_deja_entrar_al_arbol_por_ningun_otro_sitio()
    {
        // El otro lado del mismo trato, y la razón por la que el gate viejo existía: que el
        // portero no abra el árbol de servicios al exterior. Una capacidad alcanzable desde
        // internet queda protegida sólo por la llave compartida, que `CLAUDE.md` §11 dice que
        // NO es identidad.
        //
        // Sigue cubierto, y ahora sin un número: toda `reverse_proxy` hacia `api-*`/`bff-*`
        // tiene que estar en un bloque que atienda EXACTAMENTE la ruta de un webhook que
        // existe. Un `handle /v1/*` hacia `api-orders` no cuela por más que la cuenta dé uno.
        var rutas = RutasDeWebhook();
        var bloques = BloquesDelProxy();
        var intrusas = new List<string>();

        foreach (var bloque in bloques)
        {
            if (bloque.Destino is null) continue;
            if (!Regex.IsMatch(bloque.Destino, @"^(api|bff)-[\w-]+:")) continue;

            var sirve = rutas.Where(r => Atiende(r.Ruta, bloques) == bloque).ToList();

            if (sirve.Count == 0)
            {
                intrusas.Add($"`handle {bloque.Matcher}` → `{bloque.Destino}` (Caddyfile:{bloque.Linea}) "
                             + "abre el árbol de servicios sin servir ningún webhook. Lo único que puede "
                             + "salir a internet de ahí dentro es un camino de vuelta de un proveedor.");
                continue;
            }

            var ajenas = sirve.Where(r => !bloque.Destino.StartsWith($"{r.Servicio}:", StringComparison.Ordinal)).ToList();
            if (ajenas.Count > 0)
            {
                intrusas.Add($"`handle {bloque.Matcher}` → `{bloque.Destino}` (Caddyfile:{bloque.Linea}) "
                             + $"atiende rutas que no son suyas: {string.Join(", ", ajenas.Select(a => a.Ruta))}.");
            }
        }

        Assert.True(intrusas.Count == 0, string.Join(Environment.NewLine, intrusas));
    }

    [Fact]
    public void El_gate_ve_los_webhooks_que_existen()
    {
        // Sin esto, un descubrimiento roto dejaría los asserts de abajo recorriendo una lista
        // vacía y el build verde justo el día que alguien borre una verificación.
        var nombres = ConWebhook().Select(c => c.Nombre).ToList();

        Assert.Contains("Synergos.Api.Notifications", nombres);
        Assert.Contains("Synergos.Api.Payments", nombres);
    }

    [Fact]
    public void Todo_webhook_VERIFICA_LA_FIRMA_antes_de_tocar_el_almacen()
    {
        // No basta con que el verificador exista y esté probado: eso ya pasaba cuando se mutó el
        // de Api.Notifications quitando la llamada, y nada se puso rojo. Lo que se mide acá es el
        // ORDEN — que la comprobación ocurra ANTES de que el cuerpo signifique algo.
        var malas = new List<string>();

        foreach (var (nombre, dir) in ConWebhook())
        {
            // Qué sabe hacer el servicio de esta capacidad se LEE de su propio fichero: una lista
            // a mano envejece el día que alguien añade un método, y lo hace en silencio.
            var metodos = MetodosDelServicio(dir);
            Assert.True(metodos.Count > 0, $"{nombre}: no se pudo leer el servicio de la capacidad.");

            var endpoints = Directory
                .EnumerateFiles(Path.Combine(dir, "Endpoints"), "*.cs", SearchOption.AllDirectories)
                .ToList();

            var manejador = endpoints.FirstOrDefault(f => SinComentarios(f).Contains(".Verify(", StringComparison.Ordinal));

            if (manejador is null)
            {
                malas.Add($"{nombre} → expone /v1/webhooks y NADIE llama a un verificador de firma. "
                          + "Es un endpoint público, sin llave compartida, que escribe.");
                continue;
            }

            var codigo = SinComentarios(manejador);
            var verifica = codigo.IndexOf(".Verify(", StringComparison.Ordinal);

            var primerUso = metodos
                .Select(m => codigo.IndexOf($".{m}(", StringComparison.Ordinal))
                .Where(i => i >= 0)
                .DefaultIfEmpty(-1)
                .Min();

            if (primerUso >= 0 && primerUso < verifica)
            {
                malas.Add($"{nombre}/{Path.GetFileName(manejador)} → toca el servicio ANTES de "
                          + "verificar la firma. Un evento falsificado ya estaría interpretado "
                          + "cuando se descubra que no venía de nadie.");
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Una_exencion_de_la_llave_compartida_tiene_una_firma_detras()
    {
        // El otro lado del mismo trato, y el que se olvida: `UseSharedKeyAuth` acepta rutas
        // abiertas «a la vista y una por una», pero nada comprobaba que detrás de una hubiera
        // verificación. Una exención sin firma detrás es exactamente un endpoint abierto que
        // escribe — y se escribe en una línea, sin tocar ningún endpoint.
        var malas = new List<string>();

        foreach (var (nombre, dir) in Capacidades())
        {
            var programa = SinComentarios(Path.Combine(dir, "Program.cs"));
            var m = Regex.Match(programa, @"UseSharedKeyAuth\(([^;]*)\);");
            if (!m.Success) continue;

            var exentas = Regex.Matches(m.Groups[1].Value, @"""(/[^""]+)""")
                .Select(x => x.Groups[1].Value)
                .ToList();

            if (exentas.Count == 0) continue;

            var verifica = Directory.Exists(Path.Combine(dir, "Endpoints"))
                && Directory
                    .EnumerateFiles(Path.Combine(dir, "Endpoints"), "*.cs", SearchOption.AllDirectories)
                    .Any(f => SinComentarios(f).Contains(".Verify(", StringComparison.Ordinal));

            if (!verifica)
            {
                malas.Add($"{nombre} → deja {string.Join(", ", exentas)} fuera de la llave compartida "
                          + "y nadie verifica ninguna firma.");
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    /// <summary>
    /// Los métodos públicos del servicio de una capacidad — lo que «tocar el almacén» significa.
    /// </summary>
    /// <remarks>
    /// Se deducen leyendo <c>Domain/*Service.cs</c> y no de una lista escrita acá: este repo ya
    /// se equivocó <b>tres veces</b> con listas sacadas de la cabeza —«los seis de
    /// <c>Synergos.Shared</c>», «faltan las otras 16», los tres endpoints de <c>Api.Consent</c>—,
    /// y las tres veces lo que faltaba era justo el caso que importaba.
    /// </remarks>
    private static List<string> MetodosDelServicio(string dir)
        => Directory
            .EnumerateFiles(Path.Combine(dir, "Domain"), "*Service.cs")
            .SelectMany(f => Regex
                .Matches(SinComentarios(f), @"^\s{4}public\s+(?:async\s+)?[\w<>,\?\[\]\. ]+?\s+(\w+)\s*\(",
                    RegexOptions.Multiline)
                .Select(m => m.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}
