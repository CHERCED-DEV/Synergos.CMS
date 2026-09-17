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
        => Proyectos.Directorios()
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || n.StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Where(n => File.Exists(Proyectos.Dir(n!, "Program.cs")))
            .Select(n => n!.Replace("Synergos.", "").Replace(".", "-").ToLowerInvariant())
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    // ── Lo documentado y lo cableado tienen que ser lo mismo ────────────────

    /// <summary>Las variables que `.env.example` declara, por nombre.</summary>
    private static IReadOnlyList<string> VariablesDocumentadas()
        => File.ReadAllLines(Path.Combine(RepoRoot(), ".env.example"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => l.Split('=', 2)[0].Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Toda_variable_documentada_la_CONSUME_alguien()
    {
        // El defecto que evita, y que ya ocurrió DOS veces: `.env.example` promete una variable,
        // el operador la rellena en el servidor, y nadie se la pasa a nada.
        //
        // No falla y no avisa — el servicio arranca con su default y se comporta como si nadie
        // hubiera configurado nada. Pasó con `Notifications__Resend__*` (ADR 0131, desde que se
        // escribió) y con los modos de Tienda y Salud (HU #24 y #25).
        //
        // «Alguien» es el compose O un script de `tools/`: no todo lo que el servidor necesita
        // entra por un contenedor —`SYNERGOS_BACKUP_DIR` lo lee `respaldo.sh`—, y exigir que
        // TODO pase por el compose fue la primera versión de este gate. Se corrigió al primer
        // rojo, que llegó una hora después de escribirlo.
        var consumidores = new List<string> { Compose() };
        consumidores.AddRange(
            Directory.EnumerateFiles(Path.Combine(RepoRoot(), "tools"), "*.sh")
                .Select(File.ReadAllText));

        // LA ÚNICA EXCEPCIÓN, con su razón al lado y con la guarda debajo: las
        // `RCLONE_CONFIG_*` del respaldo (HU #31) las lee `rclone` de su propio entorno, no
        // nuestro código. Escribirlas en un script sólo para que este gate las viera sería
        // reenviar a rclone lo que rclone ya lee — ceremonia que además obligaría a mantener la
        // lista de opciones de cada proveedor.
        //
        // Va por PREFIJO y no por lista de nombres: el remoto se llama como el arquitecto
        // quiera, y las opciones dependen del backend que elija.
        var huerfanas = VariablesDocumentadas()
            .Where(v => !v.StartsWith("RCLONE_CONFIG_", StringComparison.Ordinal))
            .Where(v => !consumidores.Any(c => c.Contains("${" + v, StringComparison.Ordinal)))
            .ToList();

        // Y la exención no puede sobrevivir a la herramienta que la justifica. Si mañana el envío
        // deja de usar `rclone`, esas variables pasan a no consumirlas nadie y este gate tiene que
        // volver a verlas — un permiso que sobra deja de leerse (es el argumento de la lista de
        // `HttpClient` del gate #49).
        if (VariablesDocumentadas().Any(v => v.StartsWith("RCLONE_CONFIG_", StringComparison.Ordinal)))
        {
            Assert.Contains("rclone",
                File.ReadAllText(Path.Combine(RepoRoot(), "tools", "enviar-respaldo.sh")),
                StringComparison.Ordinal);
        }

        Assert.True(huerfanas.Count == 0,
            $"`.env.example` declara variables que no consume ni compose.prod.yml ni ningún "
            + $"script de tools/: {string.Join(", ", huerfanas)}. "
            + "Rellenarlas en el servidor no haría nada, y nadie se enteraría.");
    }

    [Fact]
    public void Todo_orquestador_sabe_llegar_a_SUS_capacidades()
    {
        // El peor modo de fallo del despliegue, y estuvo ahí desde que se escribió el compose:
        // sin `Capabilities__<cap>__BaseUrl`, `AddSagaMachinery` cae a `http://localhost/{cap}/`
        // — que dentro del contenedor es EL PROPIO BFF.
        //
        // El orquestador arrancaría sano, pasaría su /health, y fallaría TODAS las sagas. Parece
        // que funciona hasta que alguien compra.
        var compose = ComposeSinComentarios();

        foreach (var bff in Servicios().Where(s => s.StartsWith("bff-", StringComparison.Ordinal)))
        {
            var raiz = bff["bff-".Length..];
            var prefijo = char.ToUpperInvariant(raiz[0]) + raiz[1..];

            Assert.True(
                compose.Contains($"{prefijo}__Capabilities__", StringComparison.Ordinal),
                $"{bff} no declara ninguna Capabilities__*__BaseUrl en compose.prod.yml. "
                + "Sin eso llama a localhost, que es él mismo: arranca sano y falla todas las sagas.");

            // Y por nombre de servicio, nunca por localhost — lo mismo que ya se exige del CMS.
            var lineas = compose.Split('\n')
                .Where(l => l.Contains($"{prefijo}__Capabilities__", StringComparison.Ordinal)
                         && l.Contains("BaseUrl", StringComparison.Ordinal));

            Assert.All(lineas, l =>
                Assert.DoesNotContain("localhost", l, StringComparison.OrdinalIgnoreCase));
        }
    }

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
        var bloques = Regex.Count(texto, @"^\s{4}ports:", RegexOptions.Multiline);

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

    /// <summary>
    /// Que el proxy mande el CMS al CMS, y nada más de este fichero.
    /// </summary>
    /// <remarks>
    /// <para><b>Lo que había acá contaba rutas, y por eso cementó un defecto</b> (#114). Exigía
    /// exactamente UNA <c>reverse_proxy</c> hacia el árbol —escrito cuando el webhook era uno solo
    /// (ADR 0131)— así que cuando la HU #27 añadió el de Wompi en <c>Api.Payments</c>, escribir su
    /// ruta <b>rompía el build</b> y dejarla sin escribir mandaba el webhook al servicio
    /// equivocado.</para>
    ///
    /// <para>Quién puede entrar al árbol desde fuera lo vigila ahora <c>WebhookGateTests</c>, que
    /// lo mide por <b>cobertura</b> contra los <c>MapPost("/v1/webhooks/…")</c> que existen y no
    /// contra un número. Acá queda lo que sigue siendo del compose: que el catch-all vaya al
    /// CMS.</para>
    /// </remarks>
    [Fact]
    public void El_catch_all_del_proxy_va_al_CMS()
    {
        var caddy = File.ReadAllText(Path.Combine(RepoRoot(), "Caddyfile"));

        Assert.Contains("reverse_proxy cms:8080", caddy, StringComparison.Ordinal);
    }

    /// <summary>
    /// El proxy NO condiciona el tráfico a un endpoint que puede estar rojo por diseño.
    /// </summary>
    /// <remarks>
    /// <para><b>Esto tumbó el sitio entero y no se ve sin levantar el stack compuesto</b> (#114).
    /// El <c>Caddyfile</c> tenía <c>health_uri /_health</c> sobre el CMS. <c>/_health</c> agrega
    /// las <c>ISchemaHealthProbe</c> y devuelve <b>503 en cuanto UNA está roja</b> — y
    /// <c>bundle_registry</c> lo está por diseño mientras no se configure el CDN, que es el
    /// default. Caddy anotaba <c>status code out of tolerances</c>, marcaba el upstream caído, y
    /// contestaba <b>503 a todo</b> con el CMS vivo detrás.</para>
    ///
    /// <para><b>Cada pieza por separado parecía sana</b>, que es la firma de los defectos de este
    /// tramo: el CMS suelto contesta 200; su contenedor reporta <c>healthy</c> —el
    /// <c>HEALTHCHECK</c> del <c>Dockerfile</c> va SIN <c>--fail</c> y ahí está escrito el porqué,
    /// o sea que alguien ya sabía que <c>/_health</c> puede estar rojo sin que pase nada—; y
    /// ningún gate miraba la combinación de las dos.</para>
    ///
    /// <para><b>La ruta se DERIVA del controller</b>, no se escribe acá: el día que
    /// <c>/_health</c> se llame de otra manera, este gate la sigue. Y se comprueba que de verdad
    /// sea un endpoint capaz de contestar 503, para no estar prohibiendo una ruta inocente.</para>
    /// </remarks>
    [Fact]
    public void El_proxy_no_cuelga_el_sitio_de_una_probe_opcional()
    {
        var controller = File.ReadAllText(Path.Combine(
            RepoRoot(), "Synergos.CMS.Web", "Controllers", "HealthController.cs"));

        // Que sea lo que creemos que es: una agregación de probes que puede devolver 503.
        Assert.Contains("Status503ServiceUnavailable", controller, StringComparison.Ordinal);

        var ruta = Regex.Match(controller, @"\[Route\(""([^""]+)""\)\]").Groups[1].Value;
        Assert.False(string.IsNullOrWhiteSpace(ruta), "No se pudo leer la ruta de HealthController.");

        var caddy = File.ReadAllText(Path.Combine(RepoRoot(), "Caddyfile"));
        var chequeos = Regex.Matches(caddy, @"^\s*health_uri\s+(\S+)", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim('/'))
            .ToList();

        Assert.False(chequeos.Contains(ruta.Trim('/'), StringComparer.Ordinal),
            $"El Caddyfile cuelga el enrutado de `/{ruta.Trim('/')}`, que devuelve 503 en cuanto "
            + "UNA probe está roja — y `bundle_registry` lo está por diseño mientras no se "
            + "configure el CDN. Caddy lo lee como «no hay upstream» y contesta 503 A TODO con el "
            + "CMS vivo detrás (#114). La paciencia del primer arranque se consigue con "
            + "`lb_try_duration`, no condicionando el sitio a un subsistema opcional.");
    }

    /// <summary>
    /// El perfil que despliega producción NO puede traer la siembra de desarrollo encendida.
    /// </summary>
    /// <remarks>
    /// <para><b>Estaba encendida, y los endpoints que ese flag protege son anónimos</b> (#113).
    /// Cuatro piezas que por separado parecían inocentes: <c>appsettings.Docker.json</c> trae
    /// <c>DevSeed.Enabled = true</c>, <c>compose.prod.yml</c> corre con
    /// <c>ASPNETCORE_ENVIRONMENT: Docker</c> —o sea que <b>ése</b> es el perfil de producción—,
    /// nadie lo pisaba, y <c>DevController</c> es <c>[AllowAnonymous]</c>. Resultado:
    /// <c>POST /dev/clear-all-content</c> alcanzable desde internet, sin autenticar, para
    /// borrar el contenido entero.</para>
    ///
    /// <para><b>Y el propio controller declaraba la salvaguarda que no tenía</b>: su XML-doc
    /// dice «no-op en prod», que es cierto <i>si</i> el flag está off y asume un perfil de
    /// producción que no existía. Es la forma de #72 y #82 — la propiedad que el código
    /// anuncia como su razón de estar a salvo es justo la que no se cumple.</para>
    ///
    /// <para><b>Por qué no lo vio nadie:</b> las verificaciones en vivo de este repo se
    /// hicieron con procesos SUELTOS, y esto sólo existe en el stack COMPUESTO. Ningún gate
    /// miraba qué flags trae encendidos el perfil que se despliega — se comprobó con
    /// <c>grep -rn "DevSeed" Architecture/</c>, que daba cero.</para>
    ///
    /// <para>Se mide sobre el compose SIN comentarios, porque la prosa de arriba nombra el
    /// flag para explicarlo y un gate que se dispara con su propia documentación se acaba
    /// desactivando — la lección que este mismo fichero ya aprendió con <c>localhost</c>.</para>
    /// </remarks>
    [Fact]
    public void El_perfil_de_produccion_NO_trae_la_siembra_encendida()
    {
        var compose = ComposeSinComentarios();

        Assert.Contains("Synergos__DevSeed__Enabled:", compose, StringComparison.Ordinal);

        var linea = compose.Split('\n')
            .Single(l => l.Contains("Synergos__DevSeed__Enabled:", StringComparison.Ordinal));

        Assert.True(
            linea.Contains("\"false\"", StringComparison.OrdinalIgnoreCase)
            || linea.Contains(": false", StringComparison.OrdinalIgnoreCase),
            $"El despliegue tiene que APAGAR la siembra, y la línea dice: {linea.Trim()}. "
            + "Los endpoints de DevController son [AllowAnonymous] y uno de ellos borra todo "
            + "el contenido: encendida en producción es un borrado anónimo desde internet (#113).");
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
