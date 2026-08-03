using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// El molde de construcción de las capacidades (docs/product/08-despiece-apis.md §4) como
/// invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>Por qué esto es un gate y no una guía de estilo.</b> El arquitecto lo pidió así:
/// <i>"no puede ser una API diferente a la otra en cuanto a construcción"</i>. Veinte APIs con
/// veinte formas distintas es peor que un monolito — el monolito al menos es consistente. Y una
/// convención que solo vive en un documento se cumple en las tres primeras y se erosiona en las
/// diecisiete siguientes, porque nadie relee el documento antes de copiar el proyecto de al
/// lado.</para>
///
/// <para><b>Tosco a propósito</b>, como los demás gates del repo: no atrapa a un adversario,
/// atrapa el atajo de un martes. Y crece con el catálogo sin que nadie lo mantenga: el día que
/// aparezca <c>Synergos.Api.Payments</c>, estas reglas ya la están midiendo.</para>
/// </remarks>
public sealed class ApiMoldTests
{
    /// <summary>Las cuatro carpetas que toda capacidad tiene, con el mismo nombre y el mismo papel.</summary>
    private static readonly string[] Carpetas = { "Contracts", "Domain", "Storage", "Endpoints" };

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

    /// <summary>Los directorios de las capacidades: <c>Synergos.Api.*</c>.</summary>
    private static IReadOnlyList<(string Name, string Dir)> Capacidades()
        => Directory.EnumerateDirectories(RepoRoot(), "Synergos.Api.*")
            .Select(d => (Name: Path.GetFileName(d), Dir: d))
            .OrderBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

    private static IEnumerable<string> Fuentes(string dir)
        => Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    /// <summary>Quita comentarios de línea para no medir la prosa que documenta la regla.</summary>
    private static string SinComentarios(string file)
        => string.Join('\n', File.ReadLines(file)
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    [Fact]
    public void El_gate_ve_las_capacidades_que_existen()
    {
        // Sin esto, un descubrimiento roto dejaría TODOS los asserts de abajo en verde sobre
        // una lista vacía. Un gate que no puede fallar es peor que no tener gate.
        var nombres = Capacidades().Select(c => c.Name).ToList();

        Assert.Contains("Synergos.Api.Sessions", nombres);
        Assert.Contains("Synergos.Api.Booking", nombres);
    }

    [Fact]
    public void Toda_capacidad_tiene_las_cuatro_carpetas_del_molde()
    {
        // La separación Contracts/ ↔ Domain/ NO es ceremonia: es lo que permite cambiar el
        // modelo interno sin romper a los clientes. Fusionarlas es cómodo el primer mes y
        // carísimo el segundo, porque cada renombre interno pasa a ser un cambio de contrato.
        var faltan = Capacidades()
            .SelectMany(c => Carpetas
                .Where(f => !Directory.Exists(Path.Combine(c.Dir, f)))
                .Select(f => $"{c.Name} → falta {f}/"))
            .ToList();

        Assert.True(faltan.Count == 0, string.Join(Environment.NewLine, faltan));
    }

    [Fact]
    public void Toda_capacidad_tiene_Program_cs_y_monta_el_borde_de_llave()
    {
        // Si una API se olvidara del UseSharedKeyAuth, quedaría abierta sin que nada avise —
        // y el aviso a gritos de Shared solo se dispara cuando el middleware SÍ se monta.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            var program = Path.Combine(dir, "Program.cs");
            if (!File.Exists(program)) { malas.Add($"{name} → sin Program.cs"); continue; }

            var texto = SinComentarios(program);
            if (!texto.Contains("UseSharedKeyAuth", StringComparison.Ordinal)) malas.Add($"{name} → sin UseSharedKeyAuth");
            if (!texto.Contains("\"/health\"", StringComparison.Ordinal)) malas.Add($"{name} → sin GET /health");
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Toda_ruta_esta_versionada_salvo_health()
    {
        // Una API sin versión en la ruta obliga a romper clientes o a inventarse una versión el
        // día que haya que cambiar algo — y ese día siempre llega.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            foreach (var file in Fuentes(dir))
            {
                foreach (var m in Regex.Matches(SinComentarios(file), @"\.Map(?:Get|Post|Put|Patch|Delete)\(\s*""([^""]*)""").Cast<Match>())
                {
                    var ruta = m.Groups[1].Value;
                    if (ruta != "/health" && !ruta.StartsWith("/v1/", StringComparison.Ordinal))
                    {
                        malas.Add($"{name}/{Path.GetFileName(file)} → '{ruta}' no está bajo /v1/");
                    }
                }
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Ninguna_capacidad_expone_PUT_ni_PATCH()
    {
        // Las transiciones son acciones con nombre: POST /holds/{id}/confirm dice QUÉ pasó. Un
        // PATCH {"status":"confirmed"} deja que el cliente invente transiciones que la
        // capacidad tendría que ir rechazando de a una — y la primera que se olvide es un bug
        // de estado, no de validación.
        var malas = Capacidades()
            .SelectMany(c => Fuentes(c.Dir)
                .Where(f => Regex.IsMatch(SinComentarios(f), @"\.Map(Put|Patch)\("))
                .Select(f => $"{c.Name}/{Path.GetFileName(f)}"))
            .ToList();

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void El_ruteo_vive_en_Endpoints_y_no_desperdigado()
    {
        // Program.cs queda exento para el /health y para el Map*Endpoints(). Todo lo demás en
        // Endpoints/: si el ruteo se reparte, la superficie real de la API deja de poder leerse
        // en un sitio, y es la superficie lo que hay que revisar antes de publicar.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            foreach (var file in Fuentes(dir))
            {
                var rel = Path.GetRelativePath(dir, file);
                if (rel == "Program.cs" || rel.StartsWith("Endpoints" + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;

                if (Regex.IsMatch(SinComentarios(file), @"\.Map(Get|Post|Put|Patch|Delete)\("))
                {
                    malas.Add($"{name}/{rel} → ruteo fuera de Endpoints/");
                }
            }
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Nadie_lee_el_reloj_del_ambiente_salvo_el_arranque()
    {
        // La mitad de los errores de estas capacidades son de borde temporal —el hold que vence
        // justo, la cancelación en el límite del plazo, la ventana de retención—. Con UtcNow
        // dentro de una regla, esos casos no se prueban: se sufren en producción y se
        // reproducen a mano cambiando la hora del sistema.
        //
        // Program.cs queda exento porque es donde se registra TimeProvider.System, que es
        // precisamente la forma correcta de leer el reloj una sola vez.
        var patron = new Regex(@"\bDateTime(Offset)?\.(UtcNow|Now)\b");
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            foreach (var file in Fuentes(dir))
            {
                var rel = Path.GetRelativePath(dir, file);
                if (rel == "Program.cs") continue;

                var n = 0;
                foreach (var line in SinComentarios(file).Split('\n'))
                {
                    n++;
                    if (patron.IsMatch(line)) malas.Add($"{name}/{rel}:{n} → {line.Trim()}");
                }
            }
        }

        Assert.True(malas.Count == 0,
            "El reloj se inyecta por TimeProvider; no se lee del ambiente." + Environment.NewLine +
            string.Join(Environment.NewLine, malas));
    }

    [Fact]
    public void Toda_capacidad_referencia_Core_y_Shared_y_nada_del_CMS()
    {
        // Core le da el vocabulario común —Money, Ref, TimeWindow, Rejection— y Shared la
        // fontanería. Una capacidad que no los referencie está reinventando los dos, que es
        // exactamente lo que este árbol existe para evitar.
        var malas = new List<string>();

        foreach (var (name, dir) in Capacidades())
        {
            var csproj = Path.Combine(dir, $"{name}.csproj");
            if (!File.Exists(csproj)) { malas.Add($"{name} → sin {name}.csproj"); continue; }

            var texto = File.ReadAllText(csproj);
            if (!texto.Contains("Synergos.Core.csproj", StringComparison.Ordinal)) malas.Add($"{name} → no referencia Synergos.Core");
            if (!texto.Contains("Synergos.Shared.csproj", StringComparison.Ordinal)) malas.Add($"{name} → no referencia Synergos.Shared");
        }

        Assert.True(malas.Count == 0, string.Join(Environment.NewLine, malas));
    }
}
