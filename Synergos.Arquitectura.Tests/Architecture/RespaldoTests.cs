using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La copia de seguridad de los datos de las 22 (HU #31), como invariante ejecutable.
/// </summary>
/// <remarks>
/// <para><b>Lo que se vigila no es que exista un script de respaldo.</b> Es lo que hace que un
/// respaldo sirva el día que haga falta, que es distinto y se erosiona con más facilidad:</para>
///
/// <list type="number">
///   <item><b>Que haya restaurador.</b> Una copia que nadie restauró nunca no es una copia — es
///   una promesa que nadie comprobó. Por eso los dos ficheros se exigen juntos.</item>
///
///   <item><b>Que restaurar cueste una bandera explícita.</b> Pisa los datos vivos, y un comando
///   destructivo que se dispara con un solo argumento se dispara solo alguna vez.</item>
///
///   <item><b>Que se copie en frío.</b> <c>JsonCollectionStore</c> escribe con un <c>lock</c> de
///   proceso: copiar en caliente puede atrapar un JSON a medio escribir, y eso no da error al
///   copiar — lo da meses después, al restaurar, que es cuando no hay margen.</item>
///
///   <item><b>Que la lista de volúmenes se DERIVE del compose.</b> Una lista a mano se
///   desincroniza en la tercera ola, y lo que se pierde es justo el volumen que nadie recordó
///   añadir. <b>Y que el filtro INCLUYA por defecto</b>, que es lo que estaba mal: con
///   <c>-data$</c> se copiaban los certificados de Caddy —que se vuelven a pedir solos— y se
///   quedaban fuera la base del CMS, la biblioteca de medios, <c>App_Data</c> y el llavero de
///   DataProtection. El respaldo del producto no llevaba el producto, y no fallaba.</item>
///
///   <item><b>Que la copia SALGA de la máquina, cifrada.</b> Una copia que vive en el disco que
///   viene a proteger no protege de perder ese disco. Y sale con direcciones de entrega y
///   nombres de pacientes, así que sale cifrada o no sale.</item>
///
///   <item><b>Que la retención esté ESCRITA y no admita «para siempre».</b> Guardar
///   indefinidamente datos personales es una decisión de privacidad que nadie tomó. Con un piso
///   de copias, porque la retención por edad a secas vacía el destino el día que el respaldo
///   lleva semanas sin correr — justo el día en que hace falta.</item>
///
///   <item><b>Que exista el ENSAYO y no toque producción.</b> Una copia que nadie ha restaurado
///   nunca no es una copia: lo que hay que demostrar no es que el fichero llegó, sino que el
///   producto vuelve a leer sus datos. Y el ensayo restaura sobre un proyecto aparte — si
///   apuntara al vivo no sería un ensayo, sería el incidente.</item>
/// </list>
/// </remarks>
public sealed class RespaldoTests
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

    private static string Herramienta(string nombre)
        => File.ReadAllText(Path.Combine(RepoRoot(), "tools", nombre));

    /// <summary>El script sin sus comentarios.</summary>
    /// <remarks>
    /// <para><b>Un gate que lee código con regex tiene puntos ciegos que no se ven midiendo</b>
    /// (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>), y éste los tuvo: la
    /// comprobación de que <c>respaldo.sh</c> INVOCA al envío pasaba en verde con la llamada
    /// quitada, porque el nombre del fichero seguía apareciendo en los comentarios y en el texto
    /// que el script imprime. Se comprobó mutando.</para>
    /// </remarks>
    private static string SinComentarios(string nombre)
        => string.Join('\n', Herramienta(nombre)
            .Split('\n')
            .Where(l => !l.TrimStart().StartsWith('#')));

    /// <summary>Los volúmenes declarados en <c>compose.prod.yml</c>.</summary>
    /// <remarks>
    /// Se leen del bloque <c>volumes:</c> de nivel superior — el mismo sitio del que
    /// <c>docker compose config --volumes</c> los saca — para poder cruzar contra él lo que el
    /// respaldo excluye. Sin este cruce, la lista de exclusiones es otra lista a mano.
    /// </remarks>
    private static IReadOnlyList<string> VolumenesDelCompose()
    {
        var lineas = File.ReadAllLines(Path.Combine(RepoRoot(), "compose.prod.yml"));
        var dentro = false;
        var nombres = new List<string>();

        foreach (var linea in lineas)
        {
            if (linea.StartsWith("volumes:", StringComparison.Ordinal)) { dentro = true; continue; }
            if (!dentro) continue;
            if (linea.Length > 0 && !char.IsWhiteSpace(linea[0])) break;

            var recortada = linea.Trim();
            if (recortada.Length == 0 || recortada.StartsWith('#')) continue;
            if (recortada.EndsWith(':')) nombres.Add(recortada[..^1]);
        }

        Assert.NotEmpty(nombres);
        return nombres;
    }

    [Fact]
    public void El_respaldo_viene_CON_su_restaurador()
    {
        // Es la regla de fondo del ticket: «una copia que nadie restauró nunca no es una copia».
        // Si algún día se borrara `restaurar.sh` por «no se usa», lo que quedaría es un tar.gz
        // que nadie sabe si sirve.
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "tools", "respaldo.sh")),
            "falta tools/respaldo.sh");
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "tools", "restaurar.sh")),
            "hay respaldo y no hay restaurador: eso no es una copia, es una promesa.");
    }

    [Fact]
    public void Restaurar_EXIGE_una_bandera_explicita()
    {
        // Restaurar pisa los datos vivos. Con un solo argumento, alguien lo corre «para ver qué
        // trae» y se lleva por delante el día de trabajo de otro.
        var restaurar = Herramienta("restaurar.sh");

        Assert.Contains("--si-estoy-seguro", restaurar, StringComparison.Ordinal);
        Assert.Contains("INSPECCIÓN", restaurar, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_dos_paran_los_servicios_antes_de_tocar_un_volumen()
    {
        // En caliente se puede atrapar un JSON a medio escribir. No falla al copiar: falla al
        // restaurar, meses después.
        foreach (var script in new[] { "respaldo.sh", "restaurar.sh" })
        {
            Assert.Contains("compose stop", Herramienta(script).Replace("$COMPOSE", "compose", StringComparison.Ordinal),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void El_respaldo_arranca_de_vuelta_pase_lo_que_pase()
    {
        // Un respaldo que falla a la mitad y deja el sitio caído es peor que no haber respaldado.
        var respaldo = Herramienta("respaldo.sh");

        Assert.Contains("trap", respaldo, StringComparison.Ordinal);
        Assert.Contains("compose start", respaldo.Replace("$COMPOSE", "compose", StringComparison.Ordinal),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void La_lista_de_volumenes_se_DERIVA_del_compose()
    {
        // El día que aparezca una capacidad nueva, su volumen tiene que entrar solo. Una lista
        // escrita a mano se desincroniza, y el volumen que falta es el que nadie recuerda.
        var respaldo = Herramienta("respaldo.sh");

        Assert.Contains("config --volumes", respaldo, StringComparison.Ordinal);

        // Y que no haya una lista de capacidades a mano escondida al lado.
        Assert.DoesNotContain("api-orders-data api-cart-data", respaldo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_filtro_de_volumenes_INCLUYE_por_defecto_y_excluye_con_nombre()
    {
        // ESTE ES EL DEFECTO QUE EL ENSAYO DESTAPÓ. El criterio era `-data$`, un filtro que
        // INCLUYE: copiaba `caddy-data` —certificados que Caddy vuelve a pedir solos— y dejaba
        // fuera `cms-db` (la base de Umbraco), `cms-media` (la biblioteca), `cms-appdata` y
        // `cms-dpkeys`. Sin ese último, al restaurar no se puede descifrar la llave de firma de
        // los diplomas y el propio código avisa de que «los certificados ya emitidos dejarán de
        // verificar». O sea: el respaldo del producto no llevaba el producto, el tar salía bien,
        // y sólo se notaba restaurando.
        //
        // Lo que se vigila es el SENTIDO del filtro, no la lista: con uno que incluye, el volumen
        // que nadie recordó se pierde en silencio; con uno que excluye, se copia de más.
        var respaldo = Herramienta("respaldo.sh");

        Assert.DoesNotContain("grep -E -- '-data$'", respaldo, StringComparison.Ordinal);
        Assert.Contains("grep -E -v \"$RECONSTRUIBLES\"", respaldo, StringComparison.Ordinal);

        var reconstruibles = Regex.Match(respaldo, @"^RECONSTRUIBLES='\^\((?<lista>[^)]*)\)\$'",
            RegexOptions.Multiline).Groups["lista"].Value.Split('|', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(reconstruibles);

        // Cada exclusión tiene que existir de verdad en el compose. Una que sobra deja de leerse
        // —es el argumento de la lista de `HttpClient` del gate #49— y además esconde un typo:
        // `cms-log` en vez de `cms-logs` no excluye nada y nadie lo nota.
        var delCompose = VolumenesDelCompose();
        foreach (var r in reconstruibles)
        {
            Assert.True(delCompose.Contains(r),
                $"respaldo.sh excluye '{r}' y ese volumen no está en compose.prod.yml.");
        }

        // Y el resto entra. Si mañana aparece un volumen nuevo, entra SOLO: éste es el gate que
        // lo garantiza, y el que obliga a escribir una razón si alguien quiere dejarlo fuera.
        foreach (var v in delCompose.Where(v => !reconstruibles.Contains(v)))
        {
            Assert.DoesNotContain($"|{v}|", $"|{string.Join('|', reconstruibles)}|", StringComparison.Ordinal);
        }

        // Los cuatro que el filtro viejo perdía, nombrados: son la regresión concreta.
        foreach (var imprescindible in new[] { "cms-db", "cms-media", "cms-appdata", "cms-dpkeys" })
        {
            Assert.True(delCompose.Contains(imprescindible) && !reconstruibles.Contains(imprescindible),
                $"'{imprescindible}' tiene que entrar en el respaldo: sin él, restaurar devuelve un CMS vacío.");
        }
    }

    [Fact]
    public void La_copia_SALE_de_la_maquina_y_sale_CIFRADA()
    {
        // Una copia que vive en el disco que viene a proteger no protege de perder ese disco.
        // Y lleva direcciones de entrega y nombres de pacientes, así que sale cifrada o no sale:
        // no hay modo «sin cifrar», porque el modo sin cifrar es el que alguien deja puesto
        // «por ahora».
        var enviar = Herramienta("enviar-respaldo.sh");

        Assert.Contains("age --recipient", enviar, StringComparison.Ordinal);
        Assert.Contains("SYNERGOS_RESPALDO_LLAVE_PUBLICA", enviar, StringComparison.Ordinal);
        Assert.Contains("rclone copyto", enviar, StringComparison.Ordinal);

        // Y `respaldo.sh` tiene que INVOCARLO. Un script de envío que nadie llama es un fichero,
        // y el respaldo diario seguiría muriendo con la máquina sin que nada fallara.
        //
        // Se mira la fuente SIN COMENTARIOS a propósito: la primera versión de este gate pasaba
        // en verde con la llamada quitada, porque el nombre seguía apareciendo en la cabecera y
        // en el mensaje que el script imprime. Lo destapó mutarlo.
        Assert.Contains("\"$AQUI/enviar-respaldo.sh\" \"$ARCHIVO\"", SinComentarios("respaldo.sh"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void El_envio_FALLA_al_cablear_si_esta_configurado_a_MEDIAS()
    {
        // Es la forma del defecto #56 —el modo `Http` del CDN sin URL— y la de la llave de firma
        // de `Api.Identity`: arrancar verde, contestar que todo va bien y reventar el día que
        // alguien lo necesita. Un respaldo que termina en 0 sin haber salido es una copia que
        // nadie tiene y todos creen tener.
        //
        // Lo que NO falla es no estar configurado en absoluto: ahí `respaldo.sh` dice a gritos
        // que lo que dejó no es un respaldo, y un clon limpio sigue pudiendo copiar sus
        // volúmenes. Se elige «no pretender que hay copia» para el default y «fallar a gritos»
        // para el caso peligroso, que es el que PARECE configurado.
        var enviar = Herramienta("enviar-respaldo.sh");

        foreach (var exigido in new[]
                 {
                     "falta SYNERGOS_RESPALDO_DESTINO",
                     "falta SYNERGOS_RESPALDO_LLAVE_PUBLICA",
                 })
        {
            Assert.Contains(exigido, enviar, StringComparison.Ordinal);
        }

        // Y el respaldo grita cuando no hay destino, en vez de terminar como si hubiera copia.
        Assert.Contains("NO SALIÓ DEL SERVIDOR", Herramienta("respaldo.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void La_retencion_esta_ESCRITA_y_no_existe_el_para_siempre()
    {
        // Guardar indefinidamente un archivo con los datos personales de todo el producto es una
        // decisión de privacidad que nadie tomó, y dentro de dos años es una filtración esperando
        // a que alguien encuentre la credencial. Por eso el mecanismo NO SABE EXPRESAR «para
        // siempre»: el cero se rechaza.
        var enviar = Herramienta("enviar-respaldo.sh");

        Assert.Contains("SYNERGOS_RESPALDO_RETENCION_DIAS", enviar, StringComparison.Ordinal);
        Assert.Contains("sería «guardar para siempre»", enviar, StringComparison.Ordinal);
        Assert.Contains("rclone deletefile", enviar, StringComparison.Ordinal);

        // El piso de copias. Sin él, la retención por edad vacía el destino el día que el
        // respaldo lleva semanas sin correr: el trabajo programado que se rompió y nadie vio se
        // lleva por delante la última copia buena, en silencio.
        Assert.Contains("SYNERGOS_RESPALDO_MINIMO", enviar, StringComparison.Ordinal);
        Assert.Contains("$MINIMO", enviar, StringComparison.Ordinal);

        // Y se poda DESPUÉS de comprobar que la de hoy llegó. Podar antes es cómo se acaba con
        // cero copias la noche en que el envío falla.
        //
        // Se mide sobre la fuente sin comentarios y EXIGIENDO QUE LAS DOS EXISTAN: la primera
        // versión comparaba posiciones de dos textos, y `IndexOf` de algo ausente devuelve -1,
        // que es menor que cualquier cosa. O sea que quitar la comprobación entera dejaba el
        // gate en verde — la mutación lo destapó.
        var codigo = SinComentarios("enviar-respaldo.sh");
        var comprueba = codigo.IndexOf("ALLA=\"$(rclone lsf", StringComparison.Ordinal);
        var poda = codigo.IndexOf("| podar", StringComparison.Ordinal);

        Assert.True(comprueba >= 0, "el envío no vuelve a mirar si la copia llegó de verdad.");
        Assert.True(poda >= 0, "nadie poda: la retención es una frase sin nada que la cumpla.");
        Assert.True(comprueba < poda, "se poda antes de comprobar que la copia de hoy llegó.");

        // Las locales también caducan: guardar meses de datos personales en el mismo disco que
        // se está protegiendo no añade seguridad, sólo añade dónde perderlos.
        Assert.Contains("SYNERGOS_RESPALDO_LOCAL_DIAS", Herramienta("respaldo.sh"), StringComparison.Ordinal);

        // Y las tres van declaradas y explicadas en la plantilla del entorno.
        var plantilla = File.ReadAllText(Path.Combine(RepoRoot(), ".env.example"));
        foreach (var clave in new[]
                 {
                     "SYNERGOS_RESPALDO_DESTINO=",
                     "SYNERGOS_RESPALDO_LLAVE_PUBLICA=",
                     "SYNERGOS_RESPALDO_RETENCION_DIAS=",
                     "SYNERGOS_RESPALDO_LOCAL_DIAS=",
                     "SYNERGOS_RESPALDO_MINIMO=",
                 })
        {
            Assert.Contains(clave, plantilla, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void NINGUN_secreto_del_respaldo_entra_al_repo()
    {
        // La llave PÚBLICA de age no es secreta y por eso puede ir en la plantilla; la privada y
        // las credenciales del bucket no. La plantilla declara los nombres y deja los valores
        // vacíos, como el resto de `.env.example`.
        var plantilla = File.ReadAllText(Path.Combine(RepoRoot(), ".env.example"));

        foreach (var linea in plantilla.Split('\n'))
        {
            var limpia = linea.Trim();
            if (limpia.StartsWith('#')) continue;
            if (!limpia.StartsWith("RCLONE_CONFIG_", StringComparison.Ordinal)
                && !limpia.StartsWith("SYNERGOS_RESPALDO_LLAVE", StringComparison.Ordinal)) continue;

            Assert.EndsWith("=", limpia, StringComparison.Ordinal);
        }

        // Y una identidad de age suelta en el repo sería la llave de todos los respaldos.
        Assert.DoesNotContain("AGE-SECRET-KEY-", plantilla, StringComparison.Ordinal);
        Assert.DoesNotContain("AGE-SECRET-KEY-",
            Herramienta("enviar-respaldo.sh") + Herramienta("prueba-restauracion.sh"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void El_ensayo_TRAE_del_remoto_y_LEE_los_datos_de_vuelta()
    {
        // Lo que este ticket vino a cerrar. Que el fichero esté en el bucket no dice nada: entre
        // eso y «el producto volvió» hay cuatro cosas que pueden estar rotas sin avisar, y la
        // peor es la cuarta — los bytes perfectos y la capacidad sin poder leerlos, que es el
        // defecto #82 tal cual.
        //
        // Comprobado levantando `Api.Audit` sobre un almacén ilegible: `/health` contesta 200 y
        // la lectura 500. Un humo de salud da por buena esa restauración.
        var ensayo = Herramienta("prueba-restauracion.sh");

        // Trae del REMOTO, no del fichero local: probar contra la copia de al lado no prueba el
        // tramo que puede fallar. Es la misma trampa que un humo apuntando a localhost.
        Assert.Contains("rclone copyto \"$DESTINO/$NOMBRE\"", ensayo, StringComparison.Ordinal);
        Assert.Contains("age --decrypt", ensayo, StringComparison.Ordinal);

        // Y LEE. Sin esto es un `tar -t` con ceremonia.
        Assert.Contains("X-Synergos-Key", ensayo, StringComparison.Ordinal);
        Assert.Contains("\"total\"", ensayo, StringComparison.Ordinal);

        // Las rutas se DERIVAN del código de cada capacidad, por lo mismo que los volúmenes
        // salen del compose: la capacidad que nadie añadió a la lista es la que nadie descubre
        // que no restauró.
        Assert.Contains("MapGet", ensayo, StringComparison.Ordinal);
        Assert.Contains("Synergos.Api.*", ensayo, StringComparison.Ordinal);

        // Y exige que VUELVAN DATOS. Un sistema recién instalado contesta 200 a las dieciocho
        // colecciones; un almacén que se lee a medias, también — el conversor es tolerante.
        Assert.Contains("TODAS están vacías", ensayo, StringComparison.Ordinal);
    }

    [Fact]
    public void El_ensayo_NO_puede_apuntar_a_produccion()
    {
        // El ensayo restaura y después BORRA los volúmenes de su proyecto. Apuntado al vivo no
        // es un ensayo: es el incidente. Se comprueba, no se confía.
        var ensayo = Herramienta("prueba-restauracion.sh");

        Assert.Contains("[ \"$PROYECTO\" != \"$PRODUCCION\" ]", ensayo, StringComparison.Ordinal);
        Assert.Contains("--volumes", ensayo, StringComparison.Ordinal);

        // Y `restaurar.sh` tiene que resolver los volúmenes ANCLADOS al prefijo del proyecto.
        // Con el filtro sin anclar, `api-audit-data` casa con el volumen de producción Y con el
        // del ensayo, y `head -1` elige: el ensayo pisaría lo que venía a proteger.
        Assert.Contains("name=^${PROYECTO}_${v}$", Herramienta("restaurar.sh"), StringComparison.Ordinal);

        // La identidad no vive en el servidor: si estuviera, la máquina podría leer su propio
        // histórico y el cifrado asimétrico dejaría de comprar nada.
        Assert.Contains("o sea EN EL SERVIDOR", ensayo, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_scripts_del_respaldo_LLEGAN_al_servidor()
    {
        // Vivían sólo en el repo: la máquina que había que proteger era justo la única que no
        // tenía con qué. Un respaldo que no se puede ejecutar donde están los datos no es un
        // respaldo, es un fichero.
        var despliegue = File.ReadAllText(
            Path.Combine(RepoRoot(), ".github", "workflows", "deploy.yml"));

        foreach (var script in new[] { "tools/respaldo.sh", "tools/enviar-respaldo.sh", "tools/restaurar.sh" })
        {
            Assert.Contains(script, despliegue, StringComparison.Ordinal);
        }

        // El ensayo NO, y es deliberado: necesita la llave privada, y la llave privada no vive
        // en el servidor.
        Assert.DoesNotContain("tools/prueba-restauracion.sh", despliegue, StringComparison.Ordinal);

        // Y alguien tiene que CORRERLO. Sin programarlo no hay copias — y sin corridas tampoco
        // hay retención, porque lo que borra las viejas es la corrida siguiente.
        Assert.Contains("respaldo.sh", Herramienta("bootstrap-servidor.sh"), StringComparison.Ordinal);
        Assert.Contains("cron", Herramienta("bootstrap-servidor.sh"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Restaurar_VACIA_el_volumen_antes_de_desempacar()
    {
        // Sin esto, un fichero que existía en el servidor y no en la copia sobrevive, y queda un
        // estado mezclado: ni el de ayer ni el de hoy, y nadie puede razonar sobre él.
        Assert.Contains("rm -rf /destino", Herramienta("restaurar.sh"), StringComparison.Ordinal);
    }

    [Fact]
    public void Las_copias_NO_van_al_repo()
    {
        // `feedback_backups_external_to_repo`. Y además llevan datos personales —direcciones de
        // entrega, nombres de pacientes—: un respaldo commiteado es una filtración con historial.
        var respaldo = Herramienta("respaldo.sh");

        Assert.DoesNotContain("SYNERGOS_BACKUP_DIR:-.", respaldo, StringComparison.Ordinal);
        Assert.Contains("/var/backups/synergos", respaldo, StringComparison.Ordinal);

        // Y en disco no puede haber ninguno ya commiteado.
        var sueltos = Directory
            .EnumerateFiles(RepoRoot(), "synergos-datos-*.tar.gz", SearchOption.AllDirectories)
            .ToList();
        Assert.True(sueltos.Count == 0, $"hay respaldos dentro del repo: {string.Join(", ", sueltos)}");
    }

    [Fact]
    public void El_respaldo_deja_un_MANIFIESTO_de_que_copio()
    {
        // Dentro de seis meses hay un tar.gz y ninguna forma de saber de qué versión es ni si le
        // falta una capacidad. El manifiesto es lo que hace la copia legible sin adivinar.
        Assert.Contains("MANIFIESTO", Herramienta("respaldo.sh"), StringComparison.Ordinal);
        Assert.Contains("MANIFIESTO", Herramienta("restaurar.sh"), StringComparison.Ordinal);
    }
}
