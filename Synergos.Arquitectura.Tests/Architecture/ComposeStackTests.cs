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
///   <item><b>Dos réplicas de quien no tiene turno de escritura</b> pierden escrituras en
///   silencio. Desde el #112 eso ya <b>no</b> vale para toda capacidad —almacén por documento más
///   <c>StoreWriteGate</c>— así que lo que se vigila es quién lo <b>enchufa</b>, no una
///   prohibición plana. Ver <c>Solo_corre_con_dos_replicas_quien_TIENE_turno_de_escritura</c>.</item>
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

    /// <summary>
    /// El compose partido en bloques de nivel 2 —<c>nombre:</c> con sus líneas— con un ESCÁNER de
    /// líneas y no con un regex (#152).
    /// </summary>
    /// <remarks>
    /// <para><b>La primera versión de esto era un regex y pasaba en verde con el defecto
    /// puesto.</b> Era
    /// <c>^  (?&lt;s&gt;[a-z0-9-]+):$(?&lt;cuerpo&gt;(?:\n(?:    .*|\s*))*)</c>, y la alternativa
    /// <c>\s*</c> —que está ahí para tragarse las líneas en blanco— tiene <b>dos</b> puntos
    /// ciegos, los dos medidos sobre el fichero real y ninguno visible leyendo:</para>
    ///
    /// <list type="number">
    ///   <item><b>El cuerpo se corta en la primera línea en blanco.</b> <c>\s*</c> es codicioso y
    ///   <c>\s</c> incluye el salto de línea, así que consume <c>"\n      "</c> —el blanco y la
    ///   sangría de la línea siguiente— y deja al bucle exterior sin su <c>\n</c>. Medido:
    ///   el cuerpo de <c>bff-tienda</c> terminaba <b>23 líneas antes</b> de su
    ///   <c>replicas:</c>, y de los 26 servicios que declaran réplicas el gate sólo llegaba a
    ///   ver las de <b>diez</b>. Los cuatro orquestadores —lo único que este gate existe para
    ///   cazar— estaban entre los dieciséis invisibles.</item>
    ///
    ///   <item><b>Y se come la sangría de la cabecera siguiente</b>, por lo mismo: al fallar
    ///   <c>    .*</c> sobre <c>"  api-cart:"</c>, <c>\s*</c> casa sus dos espacios, así que el
    ///   <c>^  </c> del servicio de al lado ya está consumido y ese bloque <b>no se busca
    ///   nunca</b>. Medido: 32 de 58 claves de nivel 2.</item>
    /// </list>
    ///
    /// <para><b>Lo que enseña no es «ese regex estaba mal»</b> —es
    /// <c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c> tal cual: un regex sobre
    /// un formato con sangría significativa sale con un número plausible y nadie lo cruza. Por eso
    /// acá hay un escáner, que no tiene alternativas que ordenar, <b>y una red de seguridad</b>:
    /// los bloques que traen <c>replicas:</c> tienen que ser tantos como líneas
    /// <c>replicas:</c> hay en el fichero. Dos cuentas independientes de lo mismo es lo único que
    /// distingue «los vi todos» de «creo que los vi todos».</para>
    /// </remarks>
    private static IReadOnlyDictionary<string, string> BloquesDeServicio()
    {
        var lineas = File.ReadAllLines(Path.Combine(RepoRoot(), "compose.prod.yml"))
            .Where(l => !l.TrimStart().StartsWith('#'))
            .ToList();

        var bloques = new Dictionary<string, string>(StringComparer.Ordinal);
        string? actual = null;
        var cuerpo = new List<string>();

        void Cerrar()
        {
            if (actual is not null) bloques[actual] = string.Join('\n', cuerpo);
            cuerpo.Clear();
        }

        foreach (var linea in lineas)
        {
            var cabecera = Regex.Match(linea, @"^  (?<s>[a-z0-9-]+):\s*$");
            if (cabecera.Success)
            {
                Cerrar();
                actual = cabecera.Groups["s"].Value;
                continue;
            }

            // Una línea que empieza en la columna 0 y no está en blanco cierra la sección entera
            // (`services:`, `volumes:`, `networks:`) — sin esto, el último bloque de una sección
            // se tragaría la siguiente.
            if (linea.Length > 0 && !char.IsWhiteSpace(linea[0]))
            {
                Cerrar();
                actual = null;
                continue;
            }

            if (actual is not null) cuerpo.Add(linea);
        }

        Cerrar();

        // La red de seguridad, y el motivo por el que esto no es un regex. Se cuentan las líneas
        // `replicas:` del fichero y se exige que el escáner las haya repartido TODAS: si el corte
        // vuelve a perder cuerpos, esto se pone rojo en vez de decir «ninguno pasa de una».
        var enElFichero = lineas.Count(l => l.TrimStart().StartsWith("replicas:", StringComparison.Ordinal));
        var enLosBloques = bloques.Values.Count(c =>
            c.Split('\n').Any(l => l.TrimStart().StartsWith("replicas:", StringComparison.Ordinal)));

        Assert.True(
            enElFichero == enLosBloques,
            $"El escáner repartió {enLosBloques} bloques con `replicas:` y el fichero tiene " +
            $"{enElFichero} líneas `replicas:`. El corte por bloques está perdiendo cuerpos, que " +
            "es cómo el regex anterior dejaba pasar a los cuatro orquestadores en verde (#152).");

        return bloques;
    }

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

    /// <summary>
    /// Quién puede correr con MÁS de una réplica, derivado del disco y no de una lista (#152).
    /// </summary>
    /// <remarks>
    /// <para><b>Este gate decía otra cosa hasta el #152, y lo que decía había caducado.</b> Su
    /// razón era «<c>JsonCollectionStore</c> tiene un lock de PROCESO: dos instancias se pisan, y
    /// no da error — corrompe», y el generador la repetía <b>veinticuatro veces</b> en el compose.
    /// Era cierta, y el <b>#112 la desmontó</b>: el almacén pasó a un fichero por documento y
    /// <c>StoreWriteGate</c> subió a proceso cruzado el <c>lock</c> de capacidad entera. Medido en
    /// vivo allí: dos <c>Api.Inventory</c> sobre el mismo volumen, 400 ajustes relativos
    /// disparados de a dos — <b>sin turno: 400 respuestas 200 y 268 unidades</b> (132 escrituras
    /// perdidas, sin excepción y sin log); <b>con turno: 400 y 400</b>.</para>
    ///
    /// <para><b>Y un gate que afirma de más no se pone rojo: se cumple.</b> Nadie volvió al
    /// compose, así que el fichero que un agente lee para saber qué puede levantar le dice
    /// veinticuatro veces que no haga la única cosa que el #112 construyó — y le da una razón
    /// falsa. Es <c>feedback_a_fixture_built_on_a_neighbouring_defect_expires_with_it</c> con el
    /// sujeto cambiado: allá un test afirmaba el síntoma de un defecto de otra pieza, acá lo
    /// afirma un gate.</para>
    ///
    /// <para><b>Lo que se quita no es el valor, es la PROHIBICIÓN.</b> <c>replicas: 1</c> se
    /// queda donde nadie necesita dos: una cosa es «hoy no hace falta» y otra «no se puede».</para>
    ///
    /// <para><b>Se deriva del disco y no se enumera</b>, porque una lista se queda corta el día
    /// que un orquestador gane su turno: lo que habilita dos réplicas es <b>enchufar</b>
    /// <c>UseStoreWriteGate(</c> —la llamada, no la mención, que es la lección del #112 sobre
    /// <c>Api.Inventory</c>—. Medido hoy: 19 de 20 capacidades lo enchufan, 0 de 4
    /// orquestadores.</para>
    /// </remarks>
    private static readonly (string Servicio, string Razon)[] ActivoActivoSinTurno =
    [
        ("api-sessions",
            "su almacén AÑADE líneas a un fichero por día y nunca lee-modifica-escribe, que es " +
            "el único patrón que ya era seguro entre réplicas sin turno (CLAUDE.md §11)"),
    ];

    [Fact]
    public void Solo_corre_con_dos_replicas_quien_TIENE_turno_de_escritura()
    {
        var sinTurno = ActivoActivoSinTurno.Select(e => e.Servicio).ToHashSet(StringComparer.Ordinal);

        // Quién lo enchufa, leído del disco. Se cuenta la LLAMADA y no la mención: `Api.Inventory`
        // llegó a tener el comentario sin la llamada —una mutación que se quedó puesta— y un grep
        // de la explicación decía «19 de 19» (#112).
        var conTurno = Proyectos.Directorios()
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || n.StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Where(n => File.Exists(Proyectos.Dir(n!, "Program.cs")))
            .Where(n => File.ReadAllText(Proyectos.Dir(n!, "Program.cs"))
                            .Contains("UseStoreWriteGate(", StringComparison.Ordinal))
            .Select(n => n!.Replace("Synergos.", "").Replace(".", "-").ToLowerInvariant())
            .ToHashSet(StringComparer.Ordinal);

        // Red de seguridad: si el descubrimiento deja de ver, TODO servicio pareceria «sin turno»
        // y el gate volveria a ser la prohibicion plana que este cambio vino a quitar — en verde
        // y con la razon equivocada, que es como llego hasta acá.
        Assert.True(
            conTurno.Count >= 15,
            $"Sólo se vieron {conTurno.Count} servicios con `UseStoreWriteGate(` y son 19. El " +
            "descubrimiento está roto: sin esto el gate se convierte otra vez en «nadie pasa de " +
            "una», que es exactamente lo que el #152 vino a corregir.");

        // Cada bloque `nombre:` … `replicas: N` del compose. Va por el escáner y no por un regex:
        // ver el `<remarks>` de `BloquesDeServicio`, donde está medido lo que el regex perdía.
        var malas = new List<string>();

        foreach (var (servicio, cuerpo) in BloquesDeServicio())
        {
            var replicas = Regex.Match(cuerpo, @"replicas:\s*(\d+)");
            if (!replicas.Success) continue;
            if (int.Parse(replicas.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) <= 1) continue;

            if (conTurno.Contains(servicio) || sinTurno.Contains(servicio)) continue;

            malas.Add(servicio);
        }

        Assert.True(
            malas.Count == 0,
            $"Estos servicios corren con más de una réplica y NO tienen turno de escritura: " +
            $"{string.Join(", ", malas)}.\n\n" +
            "Desde el #112 dos réplicas de una CAPACIDAD conviven —almacén por documento más " +
            "`StoreWriteGate`— pero eso hay que ENCHUFARLO. Los cuatro orquestadores no lo " +
            "tienen y no es olvido: dentro de un paso de saga hay llamadas HTTP, así que un " +
            "turno de orquestador entero dejaría toda compra haciendo cola detrás de la que " +
            "espera a la pasarela. Lo que ahí corresponde es un turno POR SAGA, del tamaño de " +
            "`ISagaLease`, y es otro trabajo (CLAUDE.md §11). Mientras tanto, dos réplicas " +
            "avanzando la MISMA saga pierden una escritura, sin excepción y sin log.");
    }

    [Fact]
    public void El_censo_de_activo_activo_sin_turno_no_declara_lo_que_ya_no_corresponde()
    {
        // El diente de vuelta. Si `Api.Sessions` enchufa el turno algún día, su fila sobra — y una
        // entrada que sobra deja de leerse y acaba eximiendo a quien no debe (#137).
        var conTurno = Proyectos.Directorios()
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith("Synergos.Api.", StringComparison.Ordinal)
                     || n.StartsWith("Synergos.Bff.", StringComparison.Ordinal))
            .Where(n => File.Exists(Proyectos.Dir(n!, "Program.cs")))
            .ToDictionary(
                n => n!.Replace("Synergos.", "").Replace(".", "-").ToLowerInvariant(),
                n => File.ReadAllText(Proyectos.Dir(n!, "Program.cs"))
                         .Contains("UseStoreWriteGate(", StringComparison.Ordinal),
                StringComparer.Ordinal);

        foreach (var (servicio, razon) in ActivoActivoSinTurno)
        {
            Assert.True(
                conTurno.ContainsKey(servicio),
                $"El censo exime a «{servicio}» ({razon}) y ese servicio ya no existe.");

            Assert.False(
                conTurno[servicio],
                $"El censo exime a «{servicio}» por no tener turno de escritura, y hoy SÍ " +
                $"enchufa `UseStoreWriteGate(`. La fila sobra: {razon} — pero ya no hace falta " +
                "decirlo acá, porque el cruce lo ve solo.");
        }
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
