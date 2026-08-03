using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las invariantes del despliegue: qué se expone, qué persiste, y cuántas instancias corren
/// (HU #18).
/// </summary>
/// <remarks>
/// <para><b>Los tres defectos que esto vigila no rompen el despliegue.</b> Los tres lo dejan
/// funcionando y equivocado, que es peor:</para>
///
/// <list type="bullet">
///   <item><b>Una capacidad sin volumen</b> pierde sus datos en cada despliegue. No falla: el
///   contenedor nuevo arranca con el directorio vacío y se comporta como recién instalada.</item>
///
///   <item><b>Una capacidad con puerto publicado</b> queda en internet detrás de una llave
///   compartida que <c>CLAUDE.md</c> §11 dice que <i>no es identidad</i>. Contradice por
///   configuración lo que el código dice de sí mismo.</item>
///
///   <item><b>Dos réplicas</b> corrompen el almacén en silencio. <c>JsonCollectionStore</c> tiene
///   un <c>lock</c> de <b>proceso</b>: dos instancias se pisan y no dan error.</item>
/// </list>
///
/// <para><b>Y el cuarto, que es el más silencioso de todos:</b> que alguien añada una capacidad y
/// nadie regenere el compose. El servicio nuevo simplemente <i>no se despliega</i>. No falla —
/// falta.</para>
/// </remarks>
public sealed class ComposeStackTests
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

    private static string Compose() => File.ReadAllText(Path.Combine(RepoRoot(), "compose.prod.yml"));

    /// <summary>El compose sin comentarios — no se mide la prosa que documenta la regla.</summary>
    /// <remarks>
    /// Lo destapó este mismo gate al primer intento: el comentario que <b>explica</b> por qué no
    /// se usa <c>localhost</c> contiene la palabra <c>localhost</c>, y el assert cayó sobre él.
    /// Un gate que se dispara con su propia documentación se acaba desactivando.
    /// </remarks>
    private static string ComposeSinComentarios()
        => string.Join('\n', File.ReadAllLines(Path.Combine(RepoRoot(), "compose.prod.yml"))
            .Where(l => !l.TrimStart().StartsWith('#')));

    /// <summary>Los servicios desplegables, calculados como los calcula el generador.</summary>
    private static IReadOnlyList<string> Servicios()
        => Directory.EnumerateDirectories(RepoRoot())
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || n.StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Where(n => File.Exists(Path.Combine(RepoRoot(), n!, "Program.cs")))
            .Select(n => n!.Replace("Synergos.", "").Replace(".", "-").ToLowerInvariant())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void El_compose_existe_y_lista_las_23_piezas()
    {
        // Sin esto, un descubrimiento roto dejaría los asserts de abajo en verde sobre nada.
        var texto = Compose();
        var faltan = Servicios().Where(s => !texto.Contains($"\n  {s}:", StringComparison.Ordinal)).ToList();

        Assert.True(faltan.Count == 0,
            "Servicios que existen en el repo y NO están en compose.prod.yml — no se desplegarían, " +
            $"y nada se pondría rojo:{Environment.NewLine}{string.Join(Environment.NewLine, faltan)}");
        Assert.Contains("\n  cms:", texto, StringComparison.Ordinal);
        Assert.Contains("\n  proxy:", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void Solo_el_proxy_publica_puertos()
    {
        // Publicar una capacidad la deja en internet protegida solo por la llave compartida, que
        // sirve servicio↔servicio y NO contesta "quién es este usuario" (CLAUDE.md §11).
        var texto = Compose();

        // Cada bloque `ports:` va precedido del servicio al que pertenece; se cuenta cuántos hay
        // y se exige que sea exactamente uno.
        var bloques = Regex.Matches(texto, @"^\s{4}ports:", RegexOptions.Multiline).Count;

        Assert.True(bloques == 1,
            $"Hay {bloques} bloques `ports:` y debería haber UNO (el del proxy). " +
            "Una capacidad publicada queda alcanzable desde internet.");
    }

    [Fact]
    public void Toda_capacidad_persiste_su_almacen()
    {
        // El modo de fallo más caro y más silencioso: sin volumen, cada despliegue borra los
        // datos y nada avisa.
        var texto = Compose();
        var sinVolumen = Servicios()
            .Where(s => !texto.Contains($"- {s}-data:/app/data", StringComparison.Ordinal))
            .ToList();

        Assert.True(sinVolumen.Count == 0,
            "Estas capacidades no montan volumen en /app/data — perderían sus datos en cada " +
            $"despliegue:{Environment.NewLine}{string.Join(Environment.NewLine, sinVolumen)}");
    }

    [Fact]
    public void Nadie_corre_con_mas_de_una_replica()
    {
        // JsonCollectionStore tiene un lock de PROCESO. Dos instancias se pisan y no dan error:
        // corrompen. Y un rolling deploy son, por definición, dos instancias a la vez — el
        // despliegue "normal" de cualquier plataforma moderna rompe esto.
        var malas = Regex.Matches(Compose(), @"replicas:\s*(\d+)")
            .Select(m => int.Parse(m.Groups[1].Value))
            .Where(n => n != 1)
            .ToList();

        Assert.True(malas.Count == 0,
            "Hay servicios con más de una réplica. Mientras el almacén siga siendo " +
            "JsonCollectionStore, dos instancias corrompen en silencio (épica #16, CLAUDE.md §11).");
    }

    [Fact]
    public void La_etiqueta_de_la_imagen_es_variable_y_nunca_latest()
    {
        // Con `latest` no se puede saber qué está corriendo ni volver atrás — y la vuelta atrás
        // de la HU #19 depende justo de poder nombrar la imagen anterior.
        var texto = Compose();

        Assert.DoesNotContain(":latest", texto, StringComparison.Ordinal);
        Assert.Contains("${SYNERGOS_TAG}", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void El_CMS_llega_a_las_capacidades_por_nombre_de_servicio_y_no_por_localhost()
    {
        // Dentro de la red de Docker, `localhost` es el propio contenedor del CMS. Un
        // `http://localhost:5xxx` heredado de la máquina del arquitecto no falla al arrancar:
        // falla la primera vez que alguien busca algo.
        var texto = ComposeSinComentarios();

        Assert.Contains("http://api-sessions:8080", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("localhost", texto, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("127.0.0.1", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void El_compose_esta_al_dia_con_los_servicios_que_hay()
    {
        // El defecto que esto evita no rompe nada: alguien añade una capacidad, nadie regenera,
        // y el servicio nuevo simplemente NO se despliega. Falta, que es peor que romperse.
        var salida = CorrerNode(Path.Combine(RepoRoot(), "tools", "compose-gen.mjs"), "--check");

        Assert.Contains("al día", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void El_webhook_del_proveedor_es_lo_UNICO_del_arbol_que_el_proxy_deja_entrar()
    {
        // Es alcanzable a la fuerza —lo llama un tercero sin la llave compartida (ADR 0131)— y
        // lo protege su firma. Cualquier otra ruta del árbol abierta acá sería un descuido.
        var caddy = File.ReadAllText(Path.Combine(RepoRoot(), "Caddyfile"));

        Assert.Contains("/v1/webhooks/*", caddy, StringComparison.Ordinal);
        Assert.Contains("reverse_proxy api-notifications:8080", caddy, StringComparison.Ordinal);

        // Una sola ruta del árbol de servicios: la del webhook. Si aparece otra `reverse_proxy`
        // hacia `api-*` o `bff-*`, es una capacidad expuesta.
        var haciaElArbol = Regex.Matches(caddy, @"reverse_proxy\s+(api|bff)-[\w-]+:")
            .Select(m => m.Value)
            .ToList();

        Assert.True(haciaElArbol.Count == 1,
            "El proxy solo puede enrutar hacia UNA cosa del árbol de servicios: el webhook. " +
            $"Encontrado: {string.Join(", ", haciaElArbol)}");
    }

    private static string CorrerNode(string script, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = RepoRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add(script);
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = System.Diagnostics.Process.Start(psi)!;
        var salida = p.StandardOutput.ReadToEnd();
        var error = p.StandardError.ReadToEnd();
        p.WaitForExit();

        Assert.True(p.ExitCode == 0, $"{Path.GetFileName(script)} salió con {p.ExitCode}:{Environment.NewLine}{error}{salida}");
        return salida;
    }
}
