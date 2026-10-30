using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Las costuras de la salida a producción (línea 002-produccion): ficheros que se escriben por
/// separado y sólo funcionan JUNTOS el día del primer servidor.
/// </summary>
/// <remarks>
/// <para><b>Lo que se midió el 2026-10-15</b>: <c>deploy.yml</c> nunca había desplegado (todos sus
/// pasos <c>skipped</c>, sin servidor), así que el stack de producción no se había levantado nunca.
/// Y el <c>.env</c> que deja <c>bootstrap-servidor.sh</c> no traía dos de las cinco variables que
/// <c>compose.prod.yml</c> exige con <c>:?</c>: el primer <c>compose pull</c> habría muerto en el
/// servidor recién pagado. Cada fichero estaba bien leído solo; el defecto era la costura.</para>
///
/// <para>Cada prueba de acá cruza dos ficheros que tienen que decir lo mismo, y su mensaje dice qué
/// tocar. El ensayo de punta a punta (<c>.github/workflows/ensayo-despliegue.yml</c>) los corre de
/// verdad; esto es lo que impide que se separen entre ensayo y ensayo.</para>
/// </remarks>
[Collection(ComposeExclusivo.Nombre)]
public sealed class SalidaAProduccionTests
{
    private static string Leer(params string[] partes) => File.ReadAllText(Proyectos.Ruta(partes));

    /// <summary>Las variables del esqueleto de <c>.env</c> que escribe el bootstrap, con su valor tal cual.</summary>
    private static Dictionary<string, string> EsqueletoDelEnv()
    {
        var bootstrap = Leer("tools", "bootstrap-servidor.sh");
        var bloque = Regex.Match(bootstrap, @"<<ENV\r?\n(?<cuerpo>.*?)\r?\nENV\r?\n", RegexOptions.Singleline);
        Assert.True(bloque.Success,
            "No se encontró el heredoc `<<ENV … ENV` de tools/bootstrap-servidor.sh. Si se reescribió, "
            + "mové este gate con él: sin esto no vigila nada.");

        return Regex.Matches(bloque.Groups["cuerpo"].Value, @"(?m)^([A-Za-z_][A-Za-z0-9_]*)=(.*)$")
            .ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value.Trim(), StringComparer.Ordinal);
    }

    // ── El .env que deja la preparación del servidor ────────────────────────

    /// <summary>
    /// Toda variable que el compose EXIGE (<c>${X:?…}</c>) la deja el bootstrap generada, salvo el
    /// dominio, que es lo único que pone el arquitecto.
    /// </summary>
    /// <remarks>
    /// Sin esto, el primer despliegue muere en <c>compose pull</c> con «required variable … is missing
    /// a value» — antes de parar nada, eso sí, pero en el servidor recién pagado y con la guía diciendo
    /// que sólo faltaban el dominio y el correo. Las firmas OPCIONALES (entradas, contratos, diplomas)
    /// no entran: vacías, el host genera la llave y la guarda en <c>cms-dpkeys</c>, que SÍ va en el
    /// respaldo; puestas en <c>.env</c>, quedarían fuera de él.
    /// </remarks>
    [Fact]
    public void Las_obligatorias_del_compose_las_deja_el_bootstrap()
    {
        var obligatorias = Regex.Matches(Leer("compose.prod.yml"), @"\$\{([A-Z0-9_]+):\?")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(obligatorias.Count >= 4,
            $"Sólo se encontraron {obligatorias.Count} variables `${{X:?…}}` en compose.prod.yml: el "
            + "descubrimiento está roto y este gate vigilaría el vacío.");

        var env = EsqueletoDelEnv();

        Assert.True(env.ContainsKey("SYNERGOS_DOMAIN"),
            "El esqueleto de .env no nombra SYNERGOS_DOMAIN: es la única obligatoria que pone el "
            + "arquitecto, y tiene que estar escrita (vacía) para que la vea.");

        var sinGenerar = obligatorias
            .Where(v => v != "SYNERGOS_DOMAIN")
            .Where(v => !env.TryGetValue(v, out var valor) || valor.Length == 0)
            .OrderBy(v => v, StringComparer.Ordinal)
            .ToList();

        Assert.True(sinGenerar.Count == 0,
            "compose.prod.yml exige con `:?` y tools/bootstrap-servidor.sh NO deja generadas: "
            + string.Join(", ", sinGenerar) + ". El primer despliegue moriría en `compose pull` en el "
            + "servidor recién montado. Se generan en el heredoc del bootstrap (openssl rand), o —si "
            + "no se pueden inventar— se dejan fuera de los `:?` del generador (tools/compose-gen.mjs).");
    }

    /// <summary>
    /// <c>--solo-env</c> escribe el esqueleto y sale ANTES de pedir root o instalar nada: es lo que usa
    /// el ensayo para tener exactamente el <c>.env</c> del servidor.
    /// </summary>
    [Fact]
    public void El_bootstrap_sabe_dejar_SOLO_el_env_sin_ser_root()
    {
        var bootstrap = Leer("tools", "bootstrap-servidor.sh");

        var soloEnv = bootstrap.IndexOf("--solo-env\" ]", StringComparison.Ordinal);
        var root = bootstrap.IndexOf("id -u", StringComparison.Ordinal);
        var apt = bootstrap.IndexOf("apt-get", StringComparison.Ordinal);

        Assert.True(soloEnv >= 0, "tools/bootstrap-servidor.sh no tiene el modo `--solo-env <fichero>`.");
        Assert.True(soloEnv < root && soloEnv < apt,
            "El modo `--solo-env` de tools/bootstrap-servidor.sh se resuelve DESPUÉS de exigir root o de "
            + "instalar paquetes: el ensayo (y quien sólo quiera el .env) pasaría por apt, ufw y sshd.");

        // Y que los dos caminos escriban con la MISMA función: un esqueleto por camino es el mismo
        // defecto que este fichero vino a cerrar, con dos sitios donde olvidarse de una variable.
        Assert.Equal(1, Regex.Count(bootstrap, "<<ENV"));
    }

    // ── El compose, como lo lee Docker ──────────────────────────────────────

    /// <summary>
    /// Ningún NOMBRE de variable de entorno del compose lleva <c>${…}</c>: compose interpola
    /// valores, no nombres.
    /// </summary>
    /// <remarks>
    /// Medido en el primer ensayo del stack entero (2026-10-15): la llave de identidad iba en
    /// <c>IdentityTokens__Keys__${SYNERGOS_IDENTITY_ACTIVE_KID:-k1}</c>. El contenedor recibió ese
    /// nombre LITERAL; .NET lo partió por el <c>:</c> de <c>:-k1</c> y el mapa de llaves quedó
    /// vacío. <c>Api.Identity</c> murió en bucle («IdentityTokens:Keys está vacío») y los seis
    /// verificadores arrancaron sanos sin poder verificar un solo token. Leído línea a línea, el
    /// compose estaba bien: por eso nada lo veía.
    /// </remarks>
    [Fact]
    public void Ningun_nombre_de_variable_del_compose_lleva_interpolacion()
    {
        var malas = Regex.Matches(Leer("compose.prod.yml"), @"(?m)^\s+(?<nombre>[A-Za-z0-9_]*\$\{[^}]*\}[A-Za-z0-9_]*)\s*:")
            .Select(m => m.Groups["nombre"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.True(malas.Count == 0,
            "compose.prod.yml pone `${…}` en el NOMBRE de una variable: "
            + string.Join(", ", malas) + ". Compose no lo interpola: el contenedor recibe el nombre "
            + "literal y la configuración que iba ahí no llega. Se corrige en tools/compose-gen.mjs.");
    }

    // ── Los scripts del servidor saben qué versión corre ───────────────────

    /// <summary>
    /// Lo que opera el stack fuera de <c>deploy-remoto.sh</c> toma la versión de <c>tag.actual</c>,
    /// no del <c>.env</c>.
    /// </summary>
    /// <remarks>
    /// En el servidor, <c>SYNERGOS_TAG</c> sólo existe mientras corre <c>deploy-remoto.sh</c>: el
    /// <c>.env</c> la deja vacía a propósito y la versión queda escrita en <c>tag.actual</c>. Medido en
    /// el ensayo (2026-10-15): <c>importar-schema.sh</c> no podía nombrar la imagen del CMS, y
    /// <c>respaldo.sh</c> escribía <c>sha=</c> vacío en el MANIFIESTO — con lo que el ensayo de
    /// restauración, que levanta «la versión que escribió esos datos», pedía imágenes sin etiqueta.
    /// </remarks>
    [Fact]
    public void Los_scripts_del_servidor_saben_que_version_corre()
    {
        foreach (var script in new[] { "importar-schema.sh", "respaldo.sh", "restaurar.sh" })
        {
            Assert.True(Leer("tools", script).Contains("tag.actual", StringComparison.Ordinal),
                $"tools/{script} opera el stack y no lee tag.actual: en el servidor SYNERGOS_TAG está "
                + "vacía fuera de deploy-remoto.sh, y el compose no sabría qué imágenes nombrar.");
        }

        var respaldo = Leer("tools", "respaldo.sh");
        Assert.DoesNotMatch(@"sha=\$\(grep[^\n]*SYNERGOS_TAG", respaldo);
        Assert.Contains("sha=${VERSION", respaldo, StringComparison.Ordinal);

        // Y restaurar en un servidor PERDIDO —sin contenedores— no puede acabar en rojo con los datos
        // ya devueltos: `compose start` sin contenedores sale 1 (medido en el ensayo).
        Assert.Contains("com.docker.compose.oneoff=False", Leer("tools", "restaurar.sh"), StringComparison.Ordinal);

        // Y el import no deja vivo su contenedor efímero: un segundo Umbraco sobre la misma base.
        Assert.Contains("com.docker.compose.oneoff=True", Leer("tools", "importar-schema.sh"), StringComparison.Ordinal);
    }

    // ── El abandono que declara un orquestador llega a producción (NP-1) ────

    /// <summary>Un hijo de un objeto JSON sin distinguir mayúsculas, como <c>IConfiguration</c>.</summary>
    private static System.Text.Json.JsonElement? Hijo(System.Text.Json.JsonElement? padre, string clave)
    {
        if (padre is not { ValueKind: System.Text.Json.JsonValueKind.Object } objeto) return null;
        foreach (var p in objeto.EnumerateObject())
        {
            if (string.Equals(p.Name, clave, StringComparison.OrdinalIgnoreCase)) return p.Value;
        }
        return null;
    }

    /// <summary>
    /// Si un orquestador declara <c>&lt;Sección&gt;:Sweep:AbandonAfterMinutes</c> en su
    /// <c>appsettings.json</c>, el compose de producción lo lleva como default de su propia variable;
    /// si no, la base común de 60.
    /// </summary>
    /// <remarks>
    /// NP-1 (línea 005, decidido el 2026-10-15): el compose ponía a los cinco
    /// <c>${SYNERGOS_ABANDON_AFTER_MINUTES:-60}</c>, y una variable de entorno PISA el
    /// <c>appsettings.json</c>. El abandono de 15 minutos de Alquiler y Salud quedaba en 60 en
    /// producción: dos autorizaciones vivas 50 minutos de más, sin que nada fallara.
    /// </remarks>
    [Fact]
    public void El_abandono_que_declara_un_orquestador_llega_a_produccion()
    {
        var compose = Leer("compose.prod.yml");
        var orquestadores = Directory.GetDirectories(Proyectos.Ruta("backend", "orquestadores"), "Synergos.Bff.*")
            .Where(d => File.Exists(Path.Combine(d, "Program.cs"))
                        && File.ReadAllText(Path.Combine(d, "Program.cs")).Contains("AddSagaMachinery", StringComparison.Ordinal))
            .ToList();

        Assert.True(orquestadores.Count >= 5,
            $"Sólo se encontraron {orquestadores.Count} orquestadores con AddSagaMachinery: el descubrimiento está roto.");

        foreach (var dir in orquestadores)
        {
            var seccion = Path.GetFileName(dir)["Synergos.Bff.".Length..];
            int? declarado = null;
            var ajustes = Path.Combine(dir, "appsettings.json");
            if (File.Exists(ajustes))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(ajustes).TrimStart('﻿'),
                    new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
                if (Hijo(Hijo(Hijo(doc.RootElement, seccion), "Sweep"), "AbandonAfterMinutes") is { ValueKind: System.Text.Json.JsonValueKind.Number } valor)
                {
                    declarado = valor.GetInt32();
                }
            }

            var esperado = declarado is int minutos
                ? $"{seccion}__Sweep__AbandonAfterMinutes: ${{SYNERGOS_{seccion.ToUpperInvariant()}_ABANDON_AFTER_MINUTES:-{minutos}}}"
                : $"{seccion}__Sweep__AbandonAfterMinutes: ${{SYNERGOS_ABANDON_AFTER_MINUTES:-60}}";

            Assert.True(compose.Contains(esperado, StringComparison.Ordinal),
                $"{Path.GetFileName(dir)} {(declarado is null ? "no declara abandono" : $"declara {declarado} min de abandono")} "
                + $"y compose.prod.yml no lleva «{esperado}». Se regenera con `node tools/compose-gen.mjs`.");
        }
    }

    // ── La vitrina viaja en la imagen y entra sólo a propósito ─────────────

    /// <summary>Un patrón de <c>.dockerignore</c> como expresión regular: <c>**/</c>, <c>*</c> y <c>?</c>.</summary>
    private static Regex PatronDocker(string patron)
    {
        var p = patron.Trim().TrimEnd('/');
        var re = Regex.Escape(p).Replace(@"\*\*/", "(.*/)?").Replace(@"\*", "[^/]*").Replace(@"\?", "[^/]");
        return new Regex("^" + re + "(/.*)?$");
    }

    /// <summary>
    /// La imagen del CMS LLEVA la vitrina versionada (ADR 0129, decisión 002-P2) y sólo
    /// <c>importar-schema.sh --con-contenido</c> la importa.
    /// </summary>
    /// <remarks>
    /// Medido en el ensayo (2026-10-15): el <c>.dockerignore</c> excluía <c>uSync/v9/Content</c>,
    /// <c>uSync/v9/Media</c> y <c>wwwroot/media</c> desde antes de que el contenido se versionara. El
    /// import del servidor procesaba 1240 ítems —todo menos esos 288—, salía verde y la base quedaba con
    /// 0 documentos: la portada, el cartel vacío de Umbraco. Y aun con la imagen bien, el import no
    /// encendía los handlers de contenido, que sí enciende el gate que prueba que la base es derivable.
    /// </remarks>
    [Fact]
    public void La_imagen_lleva_la_vitrina_y_solo_la_importa_quien_lo_pide()
    {
        var patrones = File.ReadAllLines(Proyectos.Ruta(".dockerignore"))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith('!'))
            .ToList();

        foreach (var ruta in new[]
                 {
                     "Synergos.CMS.Web/uSync/v9/Content/inicio.config",
                     "Synergos.CMS.Web/uSync/v9/Media/foto.config",
                     "Synergos.CMS.Web/wwwroot/media/abc/foto.png",
                 })
        {
            var excluye = patrones.Where(p => PatronDocker(p).IsMatch(ruta)).ToList();
            Assert.True(excluye.Count == 0,
                $".dockerignore deja fuera de la imagen {ruta} ({string.Join(", ", excluye)}): la vitrina "
                + "versionada no llegaría al servidor y su import saldría verde con la base vacía.");
        }

        var importar = Leer("tools", "importar-schema.sh");
        Assert.Contains("--con-contenido", importar, StringComparison.Ordinal);
        Assert.Contains("ContentHandler__Enabled=$CONTENIDO", importar, StringComparison.Ordinal);
        Assert.Contains("MediaHandler__Enabled=$CONTENIDO", importar, StringComparison.Ordinal);

        // El ensayo importa con contenido: es lo que hace el arquitecto en un servidor nuevo.
        Assert.Contains("./importar-schema.sh --con-contenido", Leer(".github", "workflows", "ensayo-despliegue.yml"), StringComparison.Ordinal);
    }

    // ── El ensayo despliega como el despliegue ──────────────────────────────

    /// <summary>
    /// El ensayo copia lo que copia <c>deploy.yml</c> —extrayéndolo de él—, usa el <c>.env</c> del
    /// bootstrap y corre los mismos scripts del servidor, con el humo público.
    /// </summary>
    /// <remarks>
    /// Un ensayo con su propia lista de ficheros o su propio <c>.env</c> ensayaría otra cosa, y saldría
    /// verde el día que el despliegue de verdad no copia un script — que es como ya pasó dos veces
    /// (HU #31, #114).
    /// </remarks>
    [Fact]
    public void El_ensayo_despliega_como_el_despliegue()
    {
        var ensayo = Leer(".github", "workflows", "ensayo-despliegue.yml");
        var deploy = Leer(".github", "workflows", "deploy.yml");

        Assert.Contains(".github/workflows/deploy.yml", ensayo, StringComparison.Ordinal);
        Assert.Contains("scp -i ~/.ssh/id_deploy", deploy, StringComparison.Ordinal);

        // La lista NO se repite: los ficheros de datos que sólo nombra el `scp` no aparecen escritos
        // en el ensayo (los scripts sí pueden aparecer: el disparo `paths:` los nombra). Se sacan de la
        // propia lista, que ya trae tres (el catálogo de procesos, 009 NP-1): escritos acá, el cuarto
        // habría quedado sin vigilar.
        var datos = LoQueCopiaElDespliegue().Where(f => f.EndsWith(".json", StringComparison.Ordinal)).ToList();
        Assert.True(datos.Count >= 3, $"Sólo se encontraron {datos.Count} ficheros de datos en el scp de deploy.yml: el descubrimiento está roto.");
        foreach (var fichero in datos)
        {
            Assert.DoesNotContain(fichero, ensayo, StringComparison.Ordinal);
        }

        Assert.Contains("bootstrap-servidor.sh --solo-env", ensayo, StringComparison.Ordinal);

        foreach (var paso in new[] { "./deploy-remoto.sh", "./importar-schema.sh", "./respaldo.sh",
                                     "./restaurar.sh", "--si-estoy-seguro", "./tools/humo-publico.sh",
                                     "./provisionar-en-la-red.sh" })
        {
            Assert.Contains(paso, ensayo, StringComparison.Ordinal);
        }

        // El único `--volumes` es el de simular el disco perdido: cualquier otro borraría lo que
        // el ensayo necesita para comprobar que la restauración devolvió algo.
        Assert.Equal(1, Regex.Count(ensayo, @"down --volumes"));

        // Y el despliegue de producción ESPERA a este ensayo: `deploy.yml` excluye de su puerta los
        // checks cuyo nombre empieza por su propio prefijo, y el ensayo se llamaba así (converge,
        // 2026-10-15). Se deriva del filtro de `deploy.yml`, no se copia.
        var prefijo = Regex.Match(deploy, @"startsWith\('(?<p>[^']+)'\)");
        Assert.True(prefijo.Success, "No se encontró el prefijo que deploy.yml excluye de su puerta (startsWith('…')).");
        var nombres = Regex.Matches(ensayo, @"(?m)^    name:\s*(?<n>.+)$").Select(m => m.Groups["n"].Value.Trim()).ToList();
        Assert.NotEmpty(nombres);
        Assert.All(nombres, n => Assert.False(n.StartsWith(prefijo.Groups["p"].Value, StringComparison.Ordinal),
            $"El job del ensayo se llama «{n}», que empieza por «{prefijo.Groups["p"].Value}»: deploy.yml lo excluye de su puerta y "
            + "desplegaría sin esperar al ensayo del mismo commit."));

        // Y lo carga un navegador: el humo ve que las cabeceras están; esto, que no rompen nada.
        Assert.Contains("chromium", ensayo, StringComparison.Ordinal);
        Assert.Contains("'/umbraco'", ensayo, StringComparison.Ordinal);
    }

    /// <summary>La lista del <c>scp</c> de <c>deploy.yml</c>: lo que llega a <c>/opt/synergos</c>, aplanado.</summary>
    private static IReadOnlyList<string> LoQueCopiaElDespliegue()
    {
        var lista = Regex.Match(Leer(".github", "workflows", "deploy.yml"),
            @"scp -i ~/\.ssh/id_deploy[^\n]*\n(?<lista>.*?)""\$USUARIO@\$HOST:/opt/synergos/""", RegexOptions.Singleline);
        Assert.True(lista.Success, "No se encontró la lista del `scp` de .github/workflows/deploy.yml.");

        return lista.Groups["lista"].Value.Replace('\\', ' ')
            .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    // ── Lo que el compose monta del servidor ────────────────────────────────

    /// <summary>
    /// Lo que el compose MONTA del servidor (<c>./X</c>) lo copia el despliegue, y cada versión lo guarda y lo repone como
    /// guarda su compose (009, NP-1).
    /// </summary>
    /// <remarks>
    /// <para>El <c>Caddyfile</c> lo hacía así desde el principio; el catálogo de procesos que monta <c>bff-gobierno</c> es el
    /// segundo. Un fichero montado que el despliegue no copia deja al contenedor sin él, y el orquestador no arranca. Uno que
    /// la versión no guarda hace que la vuelta atrás monte el de la versión nueva (ver
    /// <c>Volver_a_una_version_repone_sus_ficheros_antes_de_bajar_sus_imagenes</c>).</para>
    ///
    /// <para>El nombre en <c>deploy-remoto.sh</c> puede ir literal o en una variable
    /// (<c>CATALOGO="provisionar.procesos.json"</c>): se buscan las dos formas.</para>
    /// </remarks>
    [Fact]
    public void Lo_que_el_compose_monta_del_servidor_lo_copia_el_despliegue_y_lo_guarda_cada_version()
    {
        var montados = Regex.Matches(Leer("compose.prod.yml"), @"(?m)^\s+(?:-\s+|source:\s*)\./(?<f>[^:\s]+)")
            .Select(m => m.Groups["f"].Value)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        // Red de seguridad: los dos que hay. Si el descubrimiento se rompe, esto vigilaría el vacío.
        Assert.Contains("Caddyfile", montados);
        Assert.Contains("provisionar.procesos.json", montados);

        var copiados = LoQueCopiaElDespliegue().Select(Path.GetFileName).ToHashSet(StringComparer.Ordinal);
        var remoto = Leer("tools", "deploy-remoto.sh");
        var bajar = remoto.IndexOf("$COMPOSE pull", StringComparison.Ordinal);
        var comprobar = remoto.IndexOf("NO están en $SHA", StringComparison.Ordinal);
        Assert.True(bajar >= 0 && comprobar >= 0, "No se reconocen el `pull` y la comprobación de la etiqueta en tools/deploy-remoto.sh.");

        foreach (var fichero in montados)
        {
            Assert.True(copiados.Contains(fichero),
                $"compose.prod.yml monta ./{fichero} de /opt/synergos y el `scp` de deploy.yml no lo copia: en el servidor no "
                + "existe, y el servicio que lo monta no arranca.");

            var nombres = new List<string> { Regex.Escape(fichero) };
            nombres.AddRange(Regex.Matches(remoto, $@"(?m)^(?<v>[A-Z_]+)=""{Regex.Escape(fichero)}""")
                .Select(m => @"\$" + m.Groups["v"].Value));
            var cualquiera = string.Join("|", nombres);

            var repone = Regex.Match(remoto, $@"cp [^\n]*""\$GUARDADOS/(?:{cualquiera})""[^\n]*""\$DIR/""");
            var guarda = Regex.Match(remoto, $@"cp [^\n]*""\$DIR/(?:{cualquiera})""[^\n]*""\$GUARDADOS/""");

            Assert.True(repone.Success && repone.Index < bajar,
                $"tools/deploy-remoto.sh no repone ./{fichero} de la versión que vuelve antes de bajar sus imágenes: la vuelta "
                + "atrás montaría el de la versión nueva.");
            Assert.True(guarda.Success && guarda.Index > comprobar,
                $"tools/deploy-remoto.sh no guarda ./{fichero} con la versión que quedó en pie (después de comprobar su "
                + "etiqueta): no habría a qué volver.");
        }
    }

    // ── El borde ────────────────────────────────────────────────────────────

    /// <summary>Las cabeceras de seguridad que el humo exige.</summary>
    private static readonly string[] CabecerasMinimas =
    [
        "Strict-Transport-Security",
        "X-Content-Type-Options",
        "Referrer-Policy",
        "X-Frame-Options",
        "Content-Security-Policy",
    ];

    /// <summary>
    /// El portero pone las cabeceras mínimas y el humo comprueba exactamente las que el portero pone.
    /// </summary>
    /// <remarks>
    /// Medido el 2026-10-15: cero cabeceras de seguridad en el portero y en el CMS. Que el humo las
    /// mire es lo que las mantiene: un <c>header</c> que alguien quite del Caddyfile no rompe nada
    /// visible, y sin esto el sitio seguiría verde sin ellas.
    /// </remarks>
    [Fact]
    public void Las_cabeceras_del_portero_las_comprueba_el_humo()
    {
        var caddy = Leer("Caddyfile");
        var bloque = Regex.Match(caddy, @"(?m)^\s*header\s*\{(?<cuerpo>[^}]*)\}");
        Assert.True(bloque.Success, "El Caddyfile no tiene un bloque `header { … }` con las cabeceras de seguridad.");

        var delPortero = Regex.Matches(bloque.Groups["cuerpo"].Value, @"(?m)^\s*\??([A-Z][A-Za-z-]+)\s+""")
            .Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var faltanEnElPortero = CabecerasMinimas.Where(c => !delPortero.Contains(c)).ToList();
        Assert.True(faltanEnElPortero.Count == 0,
            "El Caddyfile no pone: " + string.Join(", ", faltanEnElPortero) + ".");

        var humo = Leer("tools", "humo-publico.sh");
        var lista = Regex.Match(humo, @"for cabecera in (?<lista>[^;]+);");
        Assert.True(lista.Success, "tools/humo-publico.sh no comprueba las cabeceras (`for cabecera in …; do`).");

        var delHumo = lista.Groups["lista"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.True(delHumo.SetEquals(delPortero),
            "El humo y el portero no hablan de las mismas cabeceras. Portero: "
            + string.Join(", ", delPortero.Order()) + ". Humo: " + string.Join(", ", delHumo.Order()) + ".");

        // Y no se quita al CMS una cabecera más estricta que ponga él: `?` = sólo si no vino ya.
        Assert.DoesNotMatch(@"(?m)^\s*(Strict-Transport-Security|X-Content-Type-Options|Referrer-Policy|X-Frame-Options|Content-Security-Policy)\s+""",
            bloque.Groups["cuerpo"].Value);
    }

    // ── La vigilancia ───────────────────────────────────────────────────────

    [Fact]
    public void El_humo_programado_vigila_el_dominio_publico_y_se_salta_sin_el()
    {
        var programado = Leer(".github", "workflows", "humo-programado.yml");

        Assert.Contains("schedule:", programado, StringComparison.Ordinal);
        Assert.Contains("vars.SYNERGOS_DOMAIN", programado, StringComparison.Ordinal);
        Assert.Contains("tools/humo-publico.sh", programado, StringComparison.Ordinal);
        Assert.Contains("listo=no", programado, StringComparison.Ordinal);
        Assert.DoesNotContain("secrets.", programado, StringComparison.Ordinal);

        // La versión exigida sale del último despliegue que DESPLEGÓ, no de lo último empujado.
        Assert.Contains("Copiar y ejecutar", programado, StringComparison.Ordinal);
        Assert.Contains("Copiar y ejecutar", Leer(".github", "workflows", "deploy.yml"), StringComparison.Ordinal);
    }

    // ── El parche de .NET al día (DECISIONES.md, lote 1, decisión 3) ────────

    /// <summary>
    /// Las bases de las imágenes van por <c>major.minor</c> y se bajan siempre: cada despliegue lleva
    /// el último parche de servicio de .NET.
    /// </summary>
    /// <remarks>
    /// Decidido el 2026-10-15: el CMS no se migra antes de salir; corre sobre .NET 8 con el último
    /// parche hasta el fin de su soporte (2026-11-10). Eso sólo es verdad si la base no está fijada a un
    /// parche o a un digest, y si el constructor no reusa una copia vieja de la base.
    /// </remarks>
    [Fact]
    public void Las_imagenes_se_construyen_sobre_la_base_al_dia()
    {
        foreach (var dockerfile in new[] { "Dockerfile", "Dockerfile.service" })
        {
            var bases = Regex.Matches(Leer(dockerfile), @"(?m)^FROM\s+(\S+)").Select(m => m.Groups[1].Value).ToList();
            Assert.NotEmpty(bases);
            foreach (var b in bases.Where(b => b.StartsWith("mcr.microsoft.com/dotnet/", StringComparison.Ordinal)))
            {
                Assert.Matches(@":\d+\.\d+$", b);
            }
        }

        var images = Leer(".github", "workflows", "images.yml");
        var construcciones = Regex.Count(images, @"uses:\s*docker/build-push-action");
        Assert.True(construcciones >= 2, "images.yml: no se encontraron las construcciones (docker/build-push-action).");
        Assert.Equal(construcciones, Regex.Count(images, @"(?m)^\s*pull:\s*true\s*$"));
    }
}
