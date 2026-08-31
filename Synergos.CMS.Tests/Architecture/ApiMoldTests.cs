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

    /// <summary>
    /// Cuántos endpoints hay se CUENTA. <c>CLAUDE.md</c> tiene que decir esa cifra.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita ya ocurrió, y de la peor forma</b> (#52). <c>CLAUDE.md</c>
    /// decía 134 y eran 136 — pero el 2 no es lo interesante: <b>el desfase era constante desde
    /// dieciocho commits</b>. La cifra se movió de 132 a 134 mientras el árbol iba de 134 a 136,
    /// o sea que quien la actualizaba <b>arrastraba el error anterior en vez de contar</b>. Es
    /// exactamente lo que #50 dijo de sí mismo, «la cuenta era de memoria», en otro fichero.</para>
    ///
    /// <para><b>Por qué se cuenta así.</b> La cifra incluye los veinte <c>/health</c>: cuando fue
    /// verdad por última vez eran 112 bajo <c>/v1</c> más 20. <c>MapPut</c>/<c>MapPatch</c> entran
    /// en la cuenta aunque hoy sean cero, porque el día que alguien los añada la cifra tiene que
    /// moverse — que no existan lo vigila
    /// <c>Ninguna_capacidad_expone_PUT_ni_PATCH</c>, no éste.</para>
    ///
    /// <para><b>Y por qué el número va en la prosa y no en un fichero de datos.</b> Porque el
    /// valor de <c>CLAUDE.md</c> es que se lee de corrido: «20 capacidades, 136 endpoints» le dice
    /// a un agente el tamaño del árbol en una línea. Sacarlo a un JSON generado lo haría cierto y
    /// nadie lo leería. Se queda escrito a mano y se le pone un gate detrás, que es el trato.</para>
    /// </remarks>
    [Fact]
    public void La_cifra_de_endpoints_de_CLAUDE_md_se_cuenta_contra_el_arbol()
    {
        var cuantos = Capacidades()
            .SelectMany(c => Fuentes(c.Dir))
            .Sum(f => Regex.Matches(SinComentarios(f), @"\.Map(Get|Post|Delete|Put|Patch)\(").Count);

        // Sin esto, un descubrimiento roto dejaría el assert de abajo comparando contra cero.
        Assert.True(cuantos > 100, $"Se contaron {cuantos} endpoints: el descubrimiento está roto.");

        var claude = File.ReadAllText(Path.Combine(RepoRoot(), "CLAUDE.md"));

        foreach (var frase in new[]
                 {
                     $"LAS 20 CAPACIDADES, agnósticas. {cuantos} endpoints.",
                     $"20 capacidades ({cuantos} endpoints,",
                 })
        {
            Assert.True(claude.Contains(frase, StringComparison.Ordinal),
                $"CLAUDE.md no dice «{frase}». En el árbol hay {cuantos} endpoints "
                + "(rutas bajo /v1 más un /health por capacidad). La cifra se cuenta, no se "
                + "recuerda: si añadiste un endpoint, movela en §2 y en §11.");
        }
    }

    /// <summary>
    /// Toda capacidad tiene su fichero de reglas, y declara su prefijo de códigos.
    /// </summary>
    /// <remarks>
    /// <para><b>El defecto que evita</b> (#58). <c>CLAUDE.md</c> §3 dice que la respuesta a «¿qué
    /// rechaza esta capacidad?» está en <c>Domain/XRules.cs</c> y que <b>es el único sitio</b>.
    /// Era cierto en diecinueve: <c>Api.Sessions</c> no tenía ese fichero y su única regla vivía
    /// dentro del método del endpoint — donde no se puede probar sin levantar el host.</para>
    ///
    /// <para><b>Y el gate de las cuatro carpetas no lo veía</b>, porque <c>Domain/</c> existía:
    /// dentro estaba <c>SearchEvent.cs</c>. Medía la carpeta, no lo que §3 promete que hay en
    /// ella.</para>
    ///
    /// <para><b>Pide que el fichero EXISTA, no que ningún rechazo viva fuera.</b> La diferencia
    /// importa: <c>Api.Notifications</c> construye cinco códigos en <c>Transport/</c> —los fallos
    /// de firma del webhook de Resend— y ahí es donde corresponden, porque son del transporte y no
    /// del negocio. Un gate que prohibiera eso nacería con una lista de exenciones, y un gate con
    /// exenciones deja de leerse.</para>
    /// </remarks>
    [Fact]
    public void Toda_capacidad_tiene_su_fichero_de_reglas()
    {
        var malas = new List<string>();

        foreach (var (nombre, dir) in Capacidades())
        {
            var reglas = Directory.EnumerateFiles(Path.Combine(dir, "Domain"), "*Rules.cs").ToList();

            if (reglas.Count == 0)
            {
                malas.Add($"{nombre} no tiene Domain/*Rules.cs. Sus rechazos viven donde no se "
                          + "pueden probar sin levantar el host — es lo que costó una vuelta en "
                          + "BookingController (#36) y en la emisión de tokens (#14, rebanada 2).");
                continue;
            }

            // Sin prefijo declarado, cada rechazo escribe el suyo a mano y el día que uno se
            // teclee mal nadie lo nota: un código es una cadena hasta que alguien la agrupa.
            if (!reglas.Any(f => SinComentarios(f).Contains("CodePrefix", StringComparison.Ordinal)))
            {
                malas.Add($"{nombre} tiene fichero de reglas pero ninguno declara CodePrefix.");
            }
        }

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
