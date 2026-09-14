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
        => Directory.EnumerateDirectories(RepoRoot(), "Synergos.Api.*")
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
                || t.StartsWith("*", StringComparison.Ordinal)
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
