using System.Reflection;
using System.Text.RegularExpressions;
using Synergos.CMS.Web.Composers;
using Synergos.Shared;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que la fontanería con la que el CMS habla con el árbol de servicios no vuelva a copiarse
/// (#178).
/// </summary>
/// <remarks>
/// <para><b>Lo que había.</b> Quince registros hacia el árbol armaban a mano la misma cadena —URL
/// base, techo, llave, correlación—, la cabecera de la llave estaba escrita en trece clases, diez
/// copias de un lector de problem+json privado servían a once clientes, tres clientes re-aplicaban
/// la configuración en cada llamada, y ninguno tenía telemetría ni resiliencia. Hoy todo eso es
/// UNA pieza: <c>ClienteDelArbolDeServicios</c>, y la lectura del rechazo es
/// <c>RechazoDelArbolDeServicios</c> (#129).</para>
///
/// <para><b>Por qué hace falta un gate y no basta con la pieza.</b> La fábrica va a generar el
/// cliente del vertical siguiente, y lo más barato para ella es copiar uno de los quince: si la
/// copia es la de antes, compila, arranca y funciona — sin telemetría, sin reintento y con su
/// propia lectura del rechazo esperando divergir. Nada se pondría rojo. Esto sí.</para>
///
/// <para><b>Trinquete ABSOLUTO</b>, porque el árbol ya lo cumple: el criterio del #134. Las únicas
/// excepciones son las que no hablan con el árbol, y están en un censo con su razón y vigilado en
/// los dos sentidos.</para>
///
/// <para><b>Lo que la pieza HACE —mandar la llave y la correlación, medir antes de reintentar,
/// reintentar sólo lo repetible y pasajero, dejar el cuerpo legible— no se vigila acá</b>: lo
/// prueba <c>ClienteDelArbolDeServiciosTests</c> por la cadena real de <c>IHttpClientFactory</c>.
/// Esto vigila que nadie la esquive.</para>
/// </remarks>
public sealed class ClienteDelArbolTests
{
    /// <summary>
    /// Los <c>AddHttpClient</c> de los composers que NO son clientes del árbol, con su razón.
    /// </summary>
    /// <remarks>
    /// <para>La clave es el primer argumento tal como está escrito. Cada entrada tiene que seguir
    /// existiendo: una fila que ya no corresponde rompe el build igual que un infractor, porque un
    /// censo vigilado en un solo sentido se queda afirmando lo que ya no es.</para>
    ///
    /// <para><b>Los webhooks de avisos no están listados uno por uno</b>: se reconocen por su forma
    /// —<c>&lt;X&gt;Notifier.FactoryName</c> encadenado a <c>AddWebhookResilience()</c>— y hay piso
    /// para que esa forma no deje de casar en silencio.</para>
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, string> NoSonDelArbol =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<IBundleRegistryClient, HttpBundleRegistryClient>"] =
                "habla con el CDN, no con el árbol: la llave compartida NO puede viajar a un CDN y la "
                + "correlación se excluyó ahí a propósito (HU #28). Su resiliencia es el último "
                + "snapshot bueno (ADR 0132).",
            ["\"wompi\""] =
                "es la pasarela de un TERCERO: sin llave ni correlación, y con "
                + "AddResilienciaDeTercero(), que sí se exige abajo.",
        };

    /// <summary>
    /// Piso de los webhooks reconocidos por su forma. Eran doce al escribir esto: si la forma dejara
    /// de casar, la excepción se tragaría registros sin mirarlos.
    /// </summary>
    private const int MinimoDeWebhooks = 12;

    /// <summary>Piso de los registros por la pieza. Eran quince al escribir esto.</summary>
    private const int MinimoDeRegistrosPorLaPieza = 12;

    private const string Pieza = "ClienteDelArbolDeServicios.cs";

    private static string SinComentarios(string ruta)
    {
        var sinBloques = Regex.Replace(File.ReadAllText(ruta), @"/\*[\s\S]*?\*/", string.Empty);
        return string.Join('\n', sinBloques.Split('\n').Select(l =>
        {
            var i = l.IndexOf("//", StringComparison.Ordinal);
            return i < 0 ? l : l[..i];
        }));
    }

    private static IReadOnlyList<string> FuentesDelWeb()
        => Directory
            .EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Split(Path.DirectorySeparatorChar).Any(
                s => s.Equals("bin", StringComparison.OrdinalIgnoreCase)
                  || s.Equals("obj", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<(string Nombre, string Codigo)> Composers()
        => Directory.EnumerateFiles(Proyectos.Dir("Synergos.CMS.Web", "Composers"), "*.cs")
            .Where(f => !Path.GetFileName(f).Equals(Pieza, StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Select(f => (Path.GetFileName(f), SinComentarios(f)))
            .ToList();

    /// <summary>Desde un paréntesis de apertura, el índice de su cierre.</summary>
    private static int Cierre(string s, int abre)
    {
        var d = 0;
        for (var k = abre; k < s.Length; k++)
        {
            if (s[k] == '(') d++;
            else if (s[k] == ')' && --d == 0) return k;
        }
        return s.Length - 1;
    }

    /// <summary>La llamada entera con su cadena fluida (<c>.Xxx(...)</c>) detrás, hasta el <c>;</c>.</summary>
    private static string Cadena(string s, int desde)
    {
        var k = Cierre(s, s.IndexOf('(', desde)) + 1;
        for (;;)
        {
            var m = Regex.Match(s[k..], @"^\s*\.\s*\w+(?:<[^>]*>)?\s*\(");
            if (!m.Success) return s[desde..k];
            k = Cierre(s, k + m.Length - 1) + 1;
        }
    }

    // ── La llave compartida, en un sitio ────────────────────────────────────

    [Fact]
    public void La_llave_compartida_se_escribe_en_UN_solo_sitio_del_CMS()
    {
        var declarantes = FuentesDelWeb()
            .Where(f => SinComentarios(f).Contains("\"X-Synergos-Key\"", StringComparison.Ordinal))
            .Select(f => Path.GetFileName(f)!)
            .ToList();

        Assert.True(
            declarantes.Count == 1 && declarantes[0] == Pieza,
            "La cabecera de la llave compartida tiene que estar escrita en EXACTAMENTE un fichero del "
            + $"CMS —Composers/{Pieza}— y hoy la escriben: "
            + (declarantes.Count == 0 ? "ninguno (¿se pudrió el patrón?)" : string.Join(", ", declarantes))
            + ". Un cliente que la escribe por su cuenta es un cliente que arma su cadena a mano: "
            + "enchufá la pieza con services.AddClienteDelArbolDeServicios(…) (#178).");
    }

    [Fact]
    public void Las_cabeceras_de_la_pieza_son_las_del_otro_arbol()
    {
        // La llave la cruza SharedKeyAuthTests. Ésta es la que decide si un reintento es seguro:
        // con otro nombre la capacidad no reconocería la llave y el reintento crearía dos veces.
        Assert.Equal(IdempotencyHeader.Name, ClienteDelArbolDeServicios.CabeceraDeIdempotencia);
    }

    // ── El rechazo, leído en un sitio ───────────────────────────────────────

    [Fact]
    public void Nadie_lee_un_problem_json_por_su_cuenta()
    {
        // La forma de un lector de problem+json es un tipo que mapea `code` Y `detail`, se llamen
        // como se llamen sus propiedades: por el nombre (Code, Detail, con lectura insensible a
        // mayúsculas) o por atributo ("code", "detail"). Diez copias así había (#178).
        var tipo = new Regex(@"\b(?:record|class|struct)\s+(\w+)[^;{(]*([({])", RegexOptions.Compiled);

        var lectores = new List<string>();
        foreach (var f in FuentesDelWeb())
        {
            var codigo = SinComentarios(f);
            foreach (Match m in tipo.Matches(codigo))
            {
                var abre = m.Groups[2].Index;
                var (a, c) = m.Groups[2].Value == "(" ? ('(', ')') : ('{', '}');
                var d = 0;
                var k = abre;
                for (; k < codigo.Length; k++)
                {
                    if (codigo[k] == a) d++;
                    else if (codigo[k] == c && --d == 0) break;
                }
                var cuerpo = codigo[abre..Math.Min(k + 1, codigo.Length)];

                // Sólo el cuerpo PROPIO: un tipo grande que contenga otros no cuenta por ellos.
                if (cuerpo.Length > 600) continue;

                var code = Regex.IsMatch(cuerpo, @"\bCode\b|""code""");
                var detail = Regex.IsMatch(cuerpo, @"\bDetail\b|""detail""");
                if (code && detail) lectores.Add($"{Path.GetFileName(f)}:{m.Groups[1].Value}");
            }
        }

        Assert.True(
            lectores.Count == 1 && lectores[0] == "RechazoDelArbolDeServicios.cs:RechazoDelArbolDeServicios",
            "El rechazo de una capacidad se lee con RechazoDelArbolDeServicios.LeerAsync y con nada "
            + "más. Hoy mapean `code` y `detail`: "
            + (lectores.Count == 0 ? "ninguno (¿se pudrió el patrón?)" : string.Join(", ", lectores))
            + ". Un lector privado se desincroniza del compartido el día que el contrato cambie, y "
            + "además no ve la bandera `transient` (#129, #178).");
    }

    // ── Ningún composer arma a mano un cliente hacia el árbol ───────────────

    [Fact]
    public void Ningun_composer_arma_a_mano_un_cliente_hacia_el_arbol()
    {
        var aMano = new List<string>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        var webhooks = 0;

        foreach (var (nombre, codigo) in Composers())
        {
            foreach (Match m in Regex.Matches(codigo, @"\bAddHttpClient\s*(<[^>]+>)?\s*\("))
            {
                var cadena = Cadena(codigo, m.Index);
                var primero = m.Groups[1].Success
                    ? m.Groups[1].Value
                    : cadena[(cadena.IndexOf('(') + 1)..].Split(',', ')')[0].Trim();

                if (Regex.IsMatch(primero, @"^\w+Notifier\.FactoryName$")
                    && cadena.Contains(".AddWebhookResilience()", StringComparison.Ordinal))
                {
                    webhooks++;
                    continue;
                }

                if (NoSonDelArbol.ContainsKey(primero))
                {
                    vistos.Add(primero);
                    if (primero == "\"wompi\"" && !cadena.Contains(".AddResilienciaDeTercero()", StringComparison.Ordinal))
                    {
                        aMano.Add($"{nombre}: \"wompi\" sin AddResilienciaDeTercero() — el camino del dinero sin reintento");
                    }
                    continue;
                }

                aMano.Add($"{nombre}: AddHttpClient({primero}…)");
            }
        }

        Assert.True(webhooks >= MinimoDeWebhooks,
            $"Sólo se reconocieron {webhooks} webhooks de avisos y eran {MinimoDeWebhooks}: si su forma "
            + "cambió, esta excepción se estaría tragando registros sin mirarlos.");

        Assert.True(aMano.Count == 0,
            "Estos composers registran un cliente HTTP a mano: " + string.Join("; ", aMano)
            + ". Si habla con el árbol de servicios, se enchufa la pieza: "
            + "services.AddClienteDelArbolDeServicios(HttpX.ClientName, DestinoDelArbol.De(…)) — trae "
            + "llave, correlación, telemetría y reintento. Si habla con un tercero, va al censo "
            + "NoSonDelArbol con su razón (#178).");

        var sobran = NoSonDelArbol.Keys.Where(k => !vistos.Contains(k)).ToList();
        Assert.True(sobran.Count == 0,
            "Estas entradas del censo ya no corresponden a ningún registro: " + string.Join(", ", sobran)
            + ". Quitalas en el mismo commit.");
    }

    [Fact]
    public void Cada_cliente_del_arbol_se_registra_por_la_pieza()
    {
        // Derivado del CÓDIGO y no de una lista: cada constante *ClientName de un Http* del CMS es
        // el nombre de un cliente hacia el árbol, y tiene que registrarlo la pieza. Se comparan los
        // VALORES y no los nombres, porque la vía hotel y el carrito de Viajes comparten cliente
        // (HttpTravelCartEngine.ClientName = ViajesWire.ClientName).
        var web = typeof(ClienteDelArbolDeServicios).Assembly;

        var clientes = web.GetTypes()
            .Where(t => t.Namespace == "Synergos.CMS.Web.Services" && t.Name.StartsWith("Http", StringComparison.Ordinal))
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.Name.EndsWith("ClientName", StringComparison.Ordinal))
                .Select(f => (Donde: $"{t.Name}.{f.Name}", Valor: (string)f.GetRawConstantValue()!)))
            .ToList();

        var registrados = new HashSet<string>(StringComparer.Ordinal);
        var registros = 0;
        foreach (var (_, codigo) in Composers())
        {
            foreach (Match m in Regex.Matches(codigo, @"\bAddClienteDelArbolDeServicios\s*\(\s*(\w+)\.(\w+)\s*,"))
            {
                registros++;
                var campo = web.GetTypes()
                    .Where(t => t.Name == m.Groups[1].Value)
                    .Select(t => t.GetField(m.Groups[2].Value, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    .FirstOrDefault(f => f is not null);
                Assert.True(campo is not null, $"No se encontró {m.Groups[1].Value}.{m.Groups[2].Value} en Synergos.CMS.Web.");
                registrados.Add((string)campo!.GetRawConstantValue()!);
            }
        }

        Assert.True(clientes.Count >= 10,
            $"Sólo se descubrieron {clientes.Count} constantes *ClientName en los Http* del CMS; eran más "
            + "de diez. Si el patrón dejó de casar, este gate pasa en verde sin mirar nada (#136).");
        Assert.True(registros >= MinimoDeRegistrosPorLaPieza,
            $"Sólo {registros} registros por la pieza; eran quince. ¿Cambió la forma de la llamada?");

        var sinPieza = clientes.Where(c => !registrados.Contains(c.Valor))
            .Select(c => $"{c.Donde} (\"{c.Valor}\")")
            .ToList();

        Assert.True(sinPieza.Count == 0,
            "Estos clientes del árbol no los registra la pieza: " + string.Join(", ", sinPieza)
            + ". Sin ella salen sin telemetría ni reintento, o con una cadena copiada a mano.");
    }
}
