using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// La política de build llega ENTERA a donde se compila — o no llega: al contenedor, en el
/// <c>COPY</c> de los Dockerfiles (#156); y al compilador que compila las vistas en caliente,
/// donde <b>no puede</b> llegar entera y por eso no viaja (#157).
/// </summary>
/// <remarks>
/// <para><b>Qué costó esto.</b> La política de build de este repo vive en <b>dos</b> ficheros
/// —<c>Directory.Build.props</c> (que trae <c>TreatWarningsAsErrors</c>) y <c>.editorconfig</c>
/// (que trae nueve severidades)— y el <c>COPY</c> de los dos Dockerfiles, escrito a mano,
/// nombraba tres ficheros y <b>no el cuarto</b>. Dentro de la imagen la supresión de CS1574 no
/// existía, así que el contenedor compilaba con reglas <i>más duras</i> que ninguna máquina de
/// desarrollo y que ningún job de CI: <b>26 de 26 imágenes en rojo durante cuatro días</b>, con
/// las tres suites en verde y <c>dotnet build</c> limpio en cualquier portátil.</para>
///
/// <para><b>Y el daño real no fue el rojo, fue lo que el rojo destapó</b>: detrás de aquella
/// supresión se habían podrido <b>quince</b> crefs —cinco nombres viejos de después de un
/// rename, cuatro referencias hacia arriba que el grafo de §0.A.1 prohíbe, dos miembros de otro
/// <c>record</c> sin calificar, uno heredado de una interfaz base y tres imports ausentes—. Se
/// limpiaron los quince y la supresión <b>no volvió</b>: copiar el fichero sin limpiarlos habría
/// puesto las imágenes en verde escondiéndolos otra vez, que es la salida barata y la peor.</para>
///
/// <para><b>Los dos Dockerfiles tenían la MISMA omisión y consecuencias distintas, y eso sólo se
/// ve midiendo los dos.</b> Con <c>.editorconfig</c> fuera del árbol —o sea la condición exacta
/// del contenedor— <c>Synergos.CMS.Web</c> publica en <b>cero avisos</b>: a la imagen del web la
/// rompían únicamente los crefs. Las veinte capacidades y los cuatro orquestadores, en cambio,
/// <b>no compilan</b>: <b>12 errores CA1848 y 4 CA1873</b>, los cuatro sitios en
/// <c>Synergos.Shared</c> (<c>Correlation.cs</c> y <c>SharedKeyAuth.cs</c>), que es justo el
/// proyecto que las <b>22</b> imágenes de servicio compilan. O sea que limpiar los crefs habría
/// desbloqueado una imagen y dejado las otras veintidós en rojo, con el diagnóstico apuntando a
/// un fichero que nadie había tocado. El <c>COPY</c> no es prevención acá: es la mitad que
/// faltaba.</para>
///
/// <para><b>Y lo que el gate mide no es «que las imágenes construyan»</b> —eso lo dice el
/// workflow, y tarda— sino que <b>las dos políticas sean la misma</b>. Es la propiedad
/// verdadera: el día que las cuatro llamadas a logger se reescriban con
/// <c>LoggerMessage</c>, el rojo de arriba desaparece y la divergencia sigue ahí, esperando a
/// la próxima severidad que alguien escriba en <c>.editorconfig</c>.</para>
///
/// <para><b>Se parsea sin comentarios, y la mutación corrigió por qué.</b> Este <c>&lt;remarks&gt;</c>
/// decía que sin quitarlos el gate se engañaba con su propia explicación —la nota que el arreglo
/// escribió en el <c>Dockerfile</c> nombra <c>.editorconfig</c> con todas las letras— y
/// <b>es falso</b>: medido, con <see cref="SinComentarios"/> apagado y el <c>COPY</c> quitado, el
/// gate sigue <b>rojo</b>. Lo que lo salva no es quitar comentarios: es que el cruce lee
/// <i>líneas que empiezan por <c>COPY</c></i> y no hace un <c>Contains</c> sobre el fichero, y un
/// comentario empieza por <c>#</c>. Queda escrito porque la afirmación se escribió antes de
/// medirla, que es el defecto que este repo llama «documentación por delante del código».</para>
///
/// <para><b>Donde los comentarios SÍ mienten es un paso antes</b>, en
/// <see cref="DockerfilesQueCompilan"/>: un <c>Dockerfile</c> que sólo <i>mencione</i>
/// <c>dotnet build</c> en una nota entraría en la lista de los que compilan y se le exigiría un
/// <c>COPY</c> que no necesita — un gate que pide un cambio que no arregla nada se desactiva.
/// Por eso el filtro se queda, y por eso está dicho para qué sirve de verdad.</para>
///
/// <para><b>Los Dockerfiles se DESCUBREN, no se nombran</b> (lección del #136): se buscan por
/// nombre de fichero y se filtran por <i>lo que hacen</i> —compilar .NET—, así que un tercero
/// entra solo. Nombrarlos a mano dejaría al tercero sin vigilar el día que se escriba, que es
/// justo cuando nadie se acuerda de este test.</para>
///
/// <para><b>Red de seguridad</b>: si no se descubre ningún Dockerfile, o ningún fichero de
/// política, o la raíz no devuelve ficheros, el gate <b>falla</b>. Un gate que pasa sobre una
/// lista vacía es el modo de fallo que el #136 midió —12/12 en verde sin mirar un solo
/// proyecto— y es el que se hereda, porque un rojo se arregla y un verde no se mira.</para>
/// </remarks>
public sealed class PoliticaDeBuildEnLaImagenTests
{
    /// <summary>
    /// Los ficheros que MSBuild y Roslyn leen <b>implícitamente</b> de la raíz al compilar, y
    /// <b>qué se pierde la imagen si falta cada uno</b>.
    /// </summary>
    /// <remarks>
    /// <para>Esta lista es un hecho de .NET y no de este repo —MSBuild, Roslyn y NuGet leen
    /// estos nombres y no otros—, que es lo que hace legítimo escribirla. Pero no alcanza sola,
    /// porque no ve un fichero de política cuyo nombre no esté acá. Ese hueco lo cierra
    /// <see cref="NoAfectanALaCompilacion"/>: todo fichero de la raíz tiene que estar en UNA de
    /// las dos listas, así que el que no conozco rompe el build y obliga a decidir.</para>
    ///
    /// <para>Sólo se exige lo que <b>existe</b> en disco: nombrar un
    /// <c>Directory.Build.targets</c> que nadie escribió pondría un <c>COPY</c> muerto, que hace
    /// fallar a <c>docker build</c> con un mensaje que no dice nada.</para>
    ///
    /// <para><b>La razón de cada uno va en la fila y no en un comentario</b>, y es lo que el
    /// mensaje del rojo imprime: un gate que sólo dice «falta `.editorconfig` en el COPY» manda
    /// a alguien a averiguar por qué importaba, que es la media hora que este defecto ya costó.
    /// Viene del gate gemelo que la rama de la fábrica escribió en paralelo
    /// (<c>ConfiguracionDelBuildTests</c>, retirado al rebasar: dos gates con el mismo criterio
    /// es <c>feedback_the_same_algorithm_is_not_the_same_thing</c>).</para>
    /// </remarks>
    private static readonly (string Fichero, string Porque)[] LeidosPorElToolchain =
    [
        ("global.json",              "clava el SDK — sin él la imagen compila con otro compilador"),
        ("Directory.Build.props",    "propiedades comunes: Nullable, TreatWarningsAsErrors, NoWarn"),
        ("Directory.Build.targets",  "targets comunes, si algún día existen"),
        ("Directory.Build.rsp",      "opciones de línea de comando de MSBuild, si algún día existen"),
        ("Directory.Packages.props", "versiones centralizadas — sin él el restore no resuelve"),
        ("Directory.Solution.props", "propiedades por solución, si algún día existen"),
        (".editorconfig",            "NUEVE severidades de diagnóstico; sin él la imagen no construye (#156)"),
        (".globalconfig",            "severidades de analizador fuera de .editorconfig, si algún día existe"),
        ("nuget.config",             "fuentes de paquetes, si algún día existen"),
        ("NuGet.config",             "el mismo, con la mayúscula que usa Windows"),
        ("NuGet.Config",             "el mismo, con la grafía que escribe Visual Studio"),
    ];

    /// <summary>
    /// El censo del RESTO de la raíz: por qué ese fichero <b>no</b> cambia cómo compila el
    /// contenedor. Son <b>patrones</b> y no nombres a propósito — un <c>.sln</c> nuevo no
    /// tiene por qué pedir una línea nueva, y un fichero de un tipo que nadie censó sí.
    /// </summary>
    /// <remarks>
    /// <c>Obligatorio</c> distingue lo que el repo tiene de lo que puede aparecer en una
    /// máquina: un patrón obligatorio que ya no casa con nada <b>rompe</b>, porque una entrada
    /// que sobra deja de leerse (<c>feedback_a_census_entry_is_how_a_defect_survives_its_own_gate</c>);
    /// uno opcional describe un artefacto local y no puede exigir presencia sin ponerse rojo en
    /// el portátil de quien tenga un <c>.env</c>.
    /// </remarks>
    private static readonly (string Patron, bool Obligatorio, string Razon)[] NoAfectanALaCompilacion =
    [
        ("*.sln", true,
            "la imagen publica un .csproj por su ruta, no una solución; MSBuild resuelve los " +
            "ProjectReference por ruta y la .sln no entra en la compilación (#133)"),
        ("Dockerfile*", true,
            "la receta no se copia dentro de su propio contexto — .dockerignore la excluye"),
        ("*.md", true,
            "prosa; ningún compilador la lee"),
        (".gitignore", true,
            "herramienta de repo, no del build"),
        (".dockerignore", true,
            "decide qué ENTRA al contexto, y por eso no puede entrar él mismo"),
        ("compose*.yml", true,
            "topología del despliegue: se lee FUERA de la imagen, al levantarla"),
        ("docker-compose.yml", true,
            "el compose de desarrollo, mismo caso que compose*.yml"),
        ("Caddyfile", true,
            "configuración del proxy inverso, que es otro contenedor"),
        (".env.example", true,
            "plantilla para quien despliega; el .env real nunca viaja a una capa"),
        ("arnes.lock.json", true,
            "el SHA del arnés de agentes (#141) — lo leen las herramientas, no el compilador"),
        (".env*", false,
            "secretos locales; .dockerignore los excluye. Opcional porque en un clon limpio " +
            "no existen y exigirlos pondría rojo el gate en la máquina de quien sí los tiene"),
    ];

    private static string Raiz() => Proyectos.Raiz();

    /// <summary>Las líneas de un Dockerfile con los comentarios quitados.</summary>
    /// <remarks>
    /// Un <c>#</c> sólo abre comentario cuando es lo primero de la línea: dentro de un
    /// <c>RUN</c> puede ser parte de un comando. Se corta por eso y no por «hay un # en la
    /// línea», que se llevaría por delante a un shell legítimo.
    /// </remarks>
    private static IReadOnlyList<string> SinComentarios(string ruta)
        => File.ReadAllLines(ruta)
            .Where(l => !l.TrimStart().StartsWith('#'))
            .ToList();

    /// <summary>Los Dockerfiles que COMPILAN .NET, descubiertos del disco.</summary>
    private static IReadOnlyList<string> DockerfilesQueCompilan()
        => Directory.EnumerateFiles(Raiz(), "Dockerfile*", SearchOption.TopDirectoryOnly)
            .Where(f => SinComentarios(f).Any(l =>
                l.Contains("dotnet publish", StringComparison.Ordinal)
                || l.Contains("dotnet build", StringComparison.Ordinal)))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>Lo que un Dockerfile copia a la RAÍZ del contexto de build.</summary>
    /// <remarks>
    /// <para>Se ancla en el destino <c>./</c> —la capa de restore, que es la que lleva la
    /// configuración a la raíz del contexto— en vez de descartar orígenes por su forma. Es más
    /// estrecho y dice mejor lo que mide: un <c>COPY Synergos.CMS.Web/ Synergos.CMS.Web/</c> no
    /// entra porque su destino no es la raíz, no porque su origen lleve una barra. Viene del
    /// gate gemelo de la rama de la fábrica.</para>
    ///
    /// <para>Un <c>COPY --from=build …</c> queda fuera por el mismo criterio y además porque
    /// copia de otra ETAPA y no del contexto, así que no puede traer un fichero de política.</para>
    /// </remarks>
    private static IReadOnlySet<string> CopiadosDeLaRaiz(string dockerfile)
    {
        var sinComentarios = string.Join('\n', SinComentarios(dockerfile));

        return Regex.Matches(
                sinComentarios,
                @"^\s*COPY\s+(?<args>[^\r\n]+?)\s+\./\s*$",
                RegexOptions.Multiline,
                TimeSpan.FromSeconds(2))
            .SelectMany(m => m.Groups["args"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Where(a => !a.StartsWith("--", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Los ficheros de política que EXISTEN en la raíz, con su razón.</summary>
    private static IReadOnlyList<(string Fichero, string Porque)> PoliticaEnDisco()
        => LeidosPorElToolchain
            .Where(v => File.Exists(Path.Combine(Raiz(), v.Fichero)))
            .OrderBy(v => v.Fichero, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void El_gate_ve_lo_que_mide()
    {
        // Sin esto, un descubrimiento roto deja TODO lo de abajo en verde sobre listas vacías —
        // el modo de fallo del #136, y el que se hereda.
        var dockerfiles = DockerfilesQueCompilan();
        var politica = PoliticaEnDisco();
        var raiz = Directory.EnumerateFiles(Raiz(), "*", SearchOption.TopDirectoryOnly).ToList();

        Assert.True(
            dockerfiles.Count >= 2,
            $"Se descubrieron {dockerfiles.Count} Dockerfiles que compilan .NET y hay al menos " +
            "dos (el del web y el parametrizado de los 22 servicios). Si el descubrimiento se " +
            "rompió, los asserts de abajo pasan sin mirar nada.");

        Assert.True(
            politica.Count >= 4,
            $"Sólo se vieron {politica.Count} ficheros de política en la raíz y hay al menos " +
            "cuatro: Directory.Build.props, Directory.Packages.props, global.json y " +
            ".editorconfig.");

        Assert.True(
            raiz.Count >= 10,
            $"La raíz devolvió {raiz.Count} ficheros. Con tan pocos, el censo de abajo no está " +
            "mirando el árbol que cree.");
    }

    [Fact]
    public void Todo_fichero_de_politica_viaja_en_el_COPY_de_cada_Dockerfile_que_compila()
    {
        var politica = PoliticaEnDisco();

        foreach (var dockerfile in DockerfilesQueCompilan())
        {
            var copiados = CopiadosDeLaRaiz(dockerfile);
            var faltan = politica.Where(p => !copiados.Contains(p.Fichero)).ToList();

            // La RAZÓN de cada uno entra en el mensaje: un rojo que sólo nombra el fichero manda
            // a alguien a averiguar por qué importaba, que es media hora que este defecto ya costó.
            var detalle = string.Join(" · ", faltan.Select(f => $"`{f.Fichero}` ({f.Porque})"));

            Assert.True(
                faltan.Count == 0,
                $"«{Path.GetFileName(dockerfile)}» compila .NET y no copia {detalle}. " +
                "La política de avisos de este repo está repartida entre Directory.Build.props y " +
                ".editorconfig, y con TreatWarningsAsErrors puesto esa política DECIDE si la " +
                "imagen se construye: faltando uno, el contenedor compila bajo reglas más duras " +
                "que cualquier máquina y que el CI. No se lee como «falta un fichero», se lee " +
                "como que el código está roto — costó 26 de 26 imágenes en rojo durante cuatro " +
                "días con las tres suites en verde. Añadilo al COPY de la etapa de restore.");
        }
    }

    [Fact]
    public void Ningun_COPY_nombra_un_fichero_de_politica_que_no_existe()
    {
        // El diente de vuelta, y cierra la salida barata del de arriba: escribir el nombre en el
        // COPY sin que el fichero esté. `docker build` falla ahí con un mensaje que no nombra la
        // causa, y falla DESPUÉS de subir el contexto entero.
        var conocidos = LeidosPorElToolchain.Select(v => v.Fichero).ToHashSet(StringComparer.Ordinal);

        foreach (var dockerfile in DockerfilesQueCompilan())
        {
            var muertos = CopiadosDeLaRaiz(dockerfile)
                .Where(c => conocidos.Contains(c))
                .Where(c => !File.Exists(Path.Combine(Raiz(), c)))
                .ToList();

            Assert.True(
                muertos.Count == 0,
                $"«{Path.GetFileName(dockerfile)}» copia {string.Join(", ", muertos)} y ese " +
                "fichero no está en la raíz. Un COPY muerto rompe la construcción de la imagen " +
                "con un error que no dice cuál es la causa.");
        }
    }

    [Fact]
    public void Todo_fichero_de_la_raiz_esta_en_UNA_de_las_dos_listas()
    {
        // El hueco del primer diente: un fichero de política cuyo NOMBRE no está en
        // LeidosPorElToolchain. `.globalconfig` es el caso de manual, y no se me habría ocurrido
        // sin esta pregunta. Por eso el criterio es «todo fichero de la raíz se explica», que
        // obliga a decidir en vez de a acordarse.
        var conocidos = LeidosPorElToolchain.Select(v => v.Fichero).ToHashSet(StringComparer.Ordinal);

        var huerfanos = Directory.EnumerateFiles(Raiz(), "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(n => !conocidos.Contains(n!))
            .Where(n => !NoAfectanALaCompilacion.Any(e => Casa(n!, e.Patron)))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            huerfanos.Count == 0,
            $"La raíz tiene ficheros que ninguna de las dos listas explica: " +
            $"{string.Join(", ", huerfanos)}. O el toolchain lo lee al compilar —y entonces va " +
            "en LeidosPorElToolchain y en el COPY de los dos Dockerfiles—, o no lo lee, y " +
            "entonces va en el censo CON SU RAZÓN. No hay tercera opción: la política repartida " +
            "entre dos ficheros y copiada a medias ya costó cuatro días de imágenes en rojo.");
    }

    [Fact]
    public void El_censo_no_declara_patrones_que_ya_no_corresponden()
    {
        // Sin este diente, el censo se queda afirmando que vigila algo que ya no está — que es
        // exactamente cómo un defecto sobrevive a la auditoría que lo vio (#137). El #141 lo
        // pagó: dos filas del censo de VersionDeUmbracoTests apuntaban a un fichero que se había
        // ido a otro repo, y sólo se supo porque el diente de vuelta rompió.
        var nombres = Directory.EnumerateFiles(Raiz(), "*", SearchOption.TopDirectoryOnly)
            .Select(f => Path.GetFileName(f)!)
            .ToList();

        var sinSujeto = NoAfectanALaCompilacion
            .Where(e => e.Obligatorio)
            .Where(e => !nombres.Any(n => Casa(n, e.Patron)))
            .Select(e => e.Patron)
            .ToList();

        Assert.True(
            sinSujeto.Count == 0,
            $"El censo declara patrones que ya no casan con nada en la raíz: " +
            $"{string.Join(", ", sinSujeto)}. Una entrada que sobra deja de leerse, y un censo " +
            "vigilado en un solo sentido acaba mintiendo. Si el fichero se fue, la fila se va " +
            "con él en el MISMO commit.");
    }

    // ────────────────────────────────────────────────────────────────────────────────
    //  LA OTRA MITAD: la política tampoco puede viajar A MEDIAS (#157).
    //
    //  Los dos defectos son el mismo visto de dos lados y se escribieron el mismo día.
    //  Arriba: `.editorconfig` no llegaba al contenedor, así que la política llegaba
    //  INCOMPLETA. Acá: `TreatWarningsAsErrors` sí llega al compilador que compila las
    //  vistas EN CALIENTE —vía `Synergos.CMS.Web.deps.json` →
    //  `DependencyContextCompilationOptions`— y `NoWarn` y `Nullable` NO PUEDEN llegar,
    //  porque ese tipo no tiene campos para ellos. O sea que de las tres propiedades del
    //  #134 viaja sólo la que prohíbe.
    //
    //  El precio, medido: desde `7485e1f` (17-sep) toda página con un hero contestaba
    //  500 —CS8669 sobre un `object?` sin contexto `#nullable`, y CS0618 sobre una API
    //  obsoleta— con `dotnet build` en 0/0 y las 3288 en verde.
    // ────────────────────────────────────────────────────────────────────────────────

    /// <summary>El proyecto cuyas vistas se compilan en caliente.</summary>
    /// <remarks>
    /// Se pregunta por NOMBRE y lo encuentra <see cref="Proyectos"/> (#136): escribir la
    /// ruta funciona hoy y la paga quien reorganice.
    /// </remarks>
    private const string CompilaVistasEnCaliente = "Synergos.CMS.Web";

    [Fact]
    public void El_bit_de_warningsAsErrors_NO_viaja_al_compilador_de_vistas()
    {
        var csproj = Proyectos.Dir(CompilaVistasEnCaliente, $"{CompilaVistasEnCaliente}.csproj");

        Assert.True(File.Exists(csproj), $"No existe {csproj} — sin él este gate no mide nada.");

        var fuente = File.ReadAllText(csproj);

        // Se mira el TARGET y no el comentario: el `<remarks>` de este gate y la nota del
        // propio csproj nombran la propiedad, así que un `Contains("TreatWarningsAsErrors")`
        // pasaría en verde con el target borrado.
        var tieneTarget =
            Regex.IsMatch(
                fuente,
                @"BeforeTargets\s*=\s*""[^""]*GenerateBuildDependencyFile",
                RegexOptions.None, TimeSpan.FromSeconds(1))
            && Regex.IsMatch(
                fuente,
                @"<TreatWarningsAsErrors>\s*false\s*</TreatWarningsAsErrors>",
                RegexOptions.None, TimeSpan.FromSeconds(1));

        Assert.True(
            tieneTarget,
            $"«{CompilaVistasEnCaliente}» compila sus vistas EN CALIENTE " +
            "(`ModelsMode=InMemoryAuto` fuerza `RazorCompileOnBuild=false`), y el compilador " +
            "que lo hace lee su política de `deps.json`. Ese canal lleva " +
            "`warningsAsErrors` y NO tiene dónde llevar `NoWarn` ni `Nullable`, así que del " +
            "trinquete del #134 llegaría sólo la prohibición: todo `object?` de una vista " +
            "pasa a ser CS8669 y toda API obsoleta a CS0618, y la página contesta 500. El " +
            "target que lo corta —`TreatWarningsAsErrors=false` antes de " +
            "`GenerateBuildDependencyFile`— no está. NO baja el trinquete: corre DESPUÉS de " +
            "`Csc`, y eso se comprueba metiendo un aviso de verdad y viendo el build en rojo.");
    }

    [Fact]
    public void Y_el_artefacto_construido_lo_confirma()
    {
        // El test de arriba lee la FUENTE; éste lee lo que de verdad se produjo. Hacen falta
        // los dos: el primero sobrevive a que no haya build al lado, y el segundo es el único
        // que se entera si el SDK deja de respetar el target — que es un cambio que no ocurre
        // en este repo y sí en la máquina de quien lo compile.
        //
        // Se barre TODO `bin/` y no sólo el deps.json que la suite tiene al lado, que es como
        // estaba: con una sola configuración mirada, un `Release` con el bit puesto pasaba en
        // verde mientras `Debug` estaba bien — y la imagen se publica en Release. Viene del gate
        // gemelo de la rama de la fábrica, que ya lo hacía así.
        var bin = Path.Combine(Proyectos.Dir(CompilaVistasEnCaliente), "bin");

        var deps = Directory.Exists(bin)
            ? Directory.EnumerateFiles(
                bin, $"{CompilaVistasEnCaliente}.deps.json", SearchOption.AllDirectories).ToList()
            : [];

        Assert.True(
            deps.Count > 0,
            $"No se encontró ningún `{CompilaVistasEnCaliente}.deps.json` bajo «{bin}», así que " +
            "este gate no puede mirar el artefacto y lo dice en vez de pasar. Compilá el " +
            "proyecto Web antes de correrlo.");

        foreach (var fichero in deps)
        {
            var corta = Path.GetRelativePath(Raiz(), fichero);
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(fichero));

            if (!doc.RootElement.TryGetProperty("compilationOptions", out var opciones))
            {
                // Sin `compilationOptions` no hay contexto de compilación preservado, y sin él el
                // compilador de Razor en ejecución no arranca siquiera: eso es otro defecto —más
                // grande y bien visible— y no éste, así que no se afirma nada sobre este bit.
                continue;
            }

            var bit = opciones.TryGetProperty("warningsAsErrors", out var v) && v.GetBoolean();

            Assert.False(
                bit,
                $"«{corta}» declara `warningsAsErrors: true`, así que el compilador de vistas en " +
                "caliente va a tratar como ERROR todo aviso del código que genera — sin el " +
                "`NoWarn` ni el `Nullable` que lo acompañan en el build, porque ese canal no " +
                "puede llevarlos. Resultado medido: 500 en toda página con un hero (#157). Lo " +
                "corta el target del csproj; si se borró, el test de al lado dice cuál.");
        }
    }

    /// <summary>Glob de una sola estrella, que es todo lo que estos patrones necesitan.</summary>
    private static bool Casa(string nombre, string patron)
        => Regex.IsMatch(
            nombre,
            "^" + Regex.Escape(patron).Replace("\\*", ".*", StringComparison.Ordinal) + "$",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));
}
