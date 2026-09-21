using System.Text.Json;
using System.Text.RegularExpressions;

namespace Synergos.CMS.Tests.Architecture;

/// <summary>
/// Que el build de la IMAGEN y el build de las VISTAS no corran con una configuración distinta
/// de la del repo (#151).
/// </summary>
/// <remarks>
/// <para><b>Un defecto, dos caras, y la misma forma.</b> El #134 puso
/// <c>TreatWarningsAsErrors=true</c> en <c>Directory.Build.props</c> con el árbol entero en cero
/// avisos, así que era gratis. Lo que ese trinquete no previó es que hay <b>dos compilaciones
/// que <c>dotnet build</c> nunca hace</b>, y a las dos les llegó la severidad sin llegarles la
/// configuración que la hace inofensiva:</para>
///
/// <list type="number">
/// <item><description><b>La imagen.</b> Los dos <c>Dockerfile</c> copian tres ficheros de
/// configuración a la capa de restore —<c>global.json</c>, <c>Directory.Build.props</c>,
/// <c>Directory.Packages.props</c>— y <b>no copiaban <c>.editorconfig</c></b>, que es donde viven
/// ocho severidades. Medido reproduciendo el CI en local (mover el fichero y reconstruir en
/// Release): <c>Synergos.CMS.Interfaces</c> da <b>8 CS1574</b> y <c>Synergos.Api.Consent</c> da
/// <b>6 CA1848 + 2 CS1574 + 2 CA1873</b> — los mismos errores, fichero por fichero, que las
/// <b>26 imágenes</b> llevaban un mes escupiendo en <c>images.yml</c>.</description></item>
///
/// <item><description><b>Las vistas.</b> <c>ModelsMode=InMemoryAuto</c> obliga a
/// <c>RazorCompileOnBuild=false</c>, así que toda vista se compila <b>al servirla</b>, y ese
/// compilador lee sus opciones del bloque <c>compilationOptions</c> del <c>deps.json</c> — donde
/// el SDK copia <c>$(TreatWarningsAsErrors)</c> tal cual. Resultado: desde el #134, cualquier
/// aviso en cualquier vista devuelve <b>500</b>. Medido: con la portada sembrada, <c>/</c>
/// contestaba 500 por dos avisos de <c>SynHost/HeroBanner.cshtml</c>; cambiando <b>ese único
/// booleano</b> en el <c>deps.json</c> y sin tocar una línea de Razor, 19 785 bytes con su
/// cuerpo.</description></item>
/// </list>
///
/// <para><b>Lo que esto enseña, y por eso hay gate en vez de dos arreglos.</b> Una severidad
/// viaja por un camino y la configuración que la vuelve inofensiva viaja por otro. Cuando los
/// dos caminos no llevan lo mismo, el resultado no es un build más estricto: es un artefacto que
/// se compila con OTRAS reglas. Y ninguna de las dos caras la puede ver un <c>dotnet build</c>
/// local — una necesita Docker y la otra necesita pedir la página.</para>
///
/// <para><b>Por qué las dos caras van en el mismo fichero</b> aunque los sujetos sean distintos
/// (un <c>Dockerfile</c> y un <c>deps.json</c>): son la misma pregunta —«¿este artefacto se
/// compila con la configuración del repo?»— y separarlas dejaría a quien arregle una sin leer la
/// otra. Es el caso contrario al de
/// <c>feedback_the_same_algorithm_is_not_the_same_thing</c>: acá el sujeto es uno y los soportes
/// son dos.</para>
/// </remarks>
public sealed class ConfiguracionDelBuildTests
{
    /// <summary>
    /// Los ficheros de la raíz que CONFIGURAN una compilación, y por qué cada uno está.
    /// </summary>
    /// <remarks>
    /// <para>Es vocabulario de la cadena de herramientas —MSBuild, Roslyn y NuGet leen estos
    /// nombres y no otros—, no una lista de este repo, así que no se puede derivar de nada del
    /// disco. Lo que SÍ se deriva es cuáles de ellos <b>existen</b>: sólo se exige copiar los que
    /// están, y el segundo diente rompe si un <c>Dockerfile</c> nombra uno que ya no está. Así
    /// una entrada que sobra no se queda mintiendo, que es lo que el #137 pagó caro.</para>
    /// </remarks>
    private static readonly (string Fichero, string Porque)[] Vocabulario =
    [
        ("global.json",              "clava el SDK — sin él la imagen compila con otro compilador"),
        ("Directory.Build.props",    "propiedades comunes: Nullable, TreatWarningsAsErrors, NoWarn"),
        ("Directory.Build.targets",  "targets comunes, si algún día existen"),
        ("Directory.Packages.props", "versiones centralizadas — sin él el restore no resuelve"),
        (".editorconfig",            "OCHO severidades de diagnóstico; sin él la imagen no construye (#151)"),
        ("NuGet.config",             "fuentes de paquetes, si algún día existen"),
    ];

    private static string Raiz() => Proyectos.Raiz();

    /// <summary>Los dos Dockerfile de la raíz, derivados del disco y no nombrados.</summary>
    private static IReadOnlyList<string> Dockerfiles()
        => Directory.EnumerateFiles(Raiz(), "Dockerfile*", SearchOption.TopDirectoryOnly)
            .Where(f => !f.EndsWith(".dockerignore", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    /// <summary>Lo que la capa de restore de un Dockerfile copia al directorio de trabajo.</summary>
    /// <remarks>
    /// Se leen los <c>COPY … ./</c> de un solo nivel —los que llevan la configuración a la raíz
    /// del contexto de build—, con los comentarios quitados: los dos ficheros EXPLICAN por qué
    /// <c>.editorconfig</c> está en la lista, así que medir sobre la prosa haría que el gate se
    /// creyera su propia explicación (<c>feedback_a_gate_that_parses_source_needs_its_own_mutations</c>).
    /// </remarks>
    private static IReadOnlyCollection<string> CopiadosALaRaiz(string dockerfile)
    {
        var sinComentarios = string.Join(
            '\n',
            File.ReadAllLines(dockerfile).Where(l => !l.TrimStart().StartsWith('#')));

        return Regex.Matches(sinComentarios, @"^\s*COPY\s+(?<args>[^\r\n]+?)\s+\./\s*$", RegexOptions.Multiline)
            .SelectMany(m => m.Groups["args"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void El_gate_ve_los_Dockerfile_que_existen()
    {
        // Red de seguridad: si el descubrimiento deja de ver, los asserts de abajo pasarían en
        // verde sobre una lista vacía — el modo de fallo del #136, que se hereda en vez de
        // arreglarse.
        var encontrados = Dockerfiles().Select(Path.GetFileName).ToList();

        Assert.Contains("Dockerfile", encontrados);
        Assert.Contains("Dockerfile.service", encontrados);
    }

    [Fact]
    public void La_imagen_se_construye_con_la_MISMA_configuracion_que_el_repo()
    {
        var presentes = Vocabulario
            .Where(v => File.Exists(Path.Combine(Raiz(), v.Fichero)))
            .ToList();

        // Sin esto, un `Vocabulario` que no case con nada dejaría el cruce vacío y verde.
        Assert.True(
            presentes.Count >= 4,
            $"Sólo se vieron {presentes.Count} ficheros de configuración en la raíz: el cruce no está midiendo nada.");

        foreach (var dockerfile in Dockerfiles())
        {
            var copiados = CopiadosALaRaiz(dockerfile);
            var nombre = Path.GetFileName(dockerfile);

            foreach (var (fichero, porque) in presentes)
            {
                Assert.True(
                    copiados.Contains(fichero),
                    $"{nombre} no copia `{fichero}` a la capa de restore, y {porque}. "
                  + $"La imagen se compilaría con otra configuración que el repo — es el #151, "
                  + $"donde faltaba `.editorconfig` y las 26 imágenes llevaban un mes sin construirse.");
            }

            // …y en el otro sentido: un fichero de configuración que se copia y ya no existe
            // rompe el build de la imagen en el `COPY`, que es tarde y lejos.
            foreach (var copiado in copiados.Where(c => Vocabulario.Any(v => v.Fichero == c)))
            {
                Assert.True(
                    File.Exists(Path.Combine(Raiz(), copiado)),
                    $"{nombre} copia `{copiado}`, que ya no está en la raíz: el `COPY` falla al construir la imagen.");
            }
        }
    }

    [Fact]
    public void Las_vistas_NO_heredan_el_trinquete_de_avisos()
    {
        var web = Proyectos.Dir("Synergos.CMS.Web");
        var deps = Directory
            .EnumerateFiles(Path.Combine(web, "bin"), "Synergos.CMS.Web.deps.json", SearchOption.AllDirectories)
            .ToList();

        // Se mide el ARTEFACTO y no el `.csproj`: lo que el compilador de Razor lee en caliente
        // es este fichero, y el target que lo produce puede dejar de correr sin que el `.csproj`
        // lo diga. Si no hay ninguno, el gate no puede medir nada y lo dice en vez de pasar.
        Assert.True(
            deps.Count > 0,
            $"No se encontró ningún `Synergos.CMS.Web.deps.json` bajo {Path.Combine(web, "bin")}: "
          + "este gate no puede medir nada. Compilá el proyecto Web antes de correrlo.");

        foreach (var fichero in deps)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(fichero));

            if (!doc.RootElement.TryGetProperty("compilationOptions", out var opciones)
             || !opciones.TryGetProperty("warningsAsErrors", out var trinquete))
            {
                // Sin `compilationOptions` no hay contexto de compilación preservado, y sin él el
                // compilador de Razor en runtime no arranca: eso es otro defecto, no este.
                continue;
            }

            Assert.False(
                trinquete.GetBoolean(),
                $"`{fichero}` declara `compilationOptions.warningsAsErrors: true`. Las vistas se "
              + "compilan EN CALIENTE contra esas opciones, así que cualquier aviso en cualquier "
              + "`.cshtml` devuelve 500 — y ningún `dotnet build` lo ve, porque compila cero "
              + "vistas. Falta el target `LasVistasNoHeredanElTrinqueteDeAvisos` de "
              + "`Synergos.CMS.Web.csproj` (#151).");
        }
    }
}
